using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods.Modpacks;

public class CurseForgeModpackImporterTests
{
    [Fact]
    public async Task ImportAsync_BuildsInstance_ResolvesFileIds_CopiesOverrides()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "pack.zip");
            BuildCurseForgeZip(archivePath,
                manifestJson: """
                {
                  "minecraft": {
                    "version": "1.20.1",
                    "modLoaders": [
                      { "id": "forge-47.2.0", "primary": true }
                    ]
                  },
                  "manifestType": "minecraftModpack",
                  "manifestVersion": 1,
                  "name": "Cool Pack",
                  "version": "1.4.2",
                  "files": [
                    { "projectID": 1001, "fileID": 5050, "required": true },
                    { "projectID": 1002, "fileID": 6060, "required": false }
                  ],
                  "overrides": "overrides"
                }
                """,
                overrides: new[]
                {
                    ("overrides/config/cool.toml", "key = 1"),
                    ("overrides/scripts/init.lua", "print('hi')"),
                });

            // Stub IModRepository: returns a known ModFile for project 1001 / file 5050,
            // streams a deterministic byte payload.
            var byteSequence = Encoding.UTF8.GetBytes("CF-BYTES");
            var stub = new StubRepository(
                listFiles: (modId, _, _, _) =>
                {
                    Assert.Equal("1001", modId);
                    return Task.FromResult<IReadOnlyList<ModFile>>(new[]
                    {
                        new ModFile
                        {
                            ModId = modId,
                            FileId = "5050",
                            DisplayName = "5050",
                            Filename = "coolmod-1.0.jar",
                            DownloadUrl = "https://edge.forgecdn.net/files/5050/coolmod-1.0.jar",
                        }
                    });
                },
                download: async (file, dest, _, ct) =>
                {
                    await dest.WriteAsync(byteSequence, 0, byteSequence.Length, ct);
                });
            var importer = new CurseForgeModpackImporter(Path.Combine(workdir, "instances"), stub);

            // Act
            var instance = await importer.ImportAsync(archivePath, targetInstanceName: null,
                progress: null, cancellationToken: CancellationToken.None);

            Assert.Equal("Cool Pack", instance.Name);
            Assert.Equal("1.20.1", instance.VersionId);
            Assert.Equal(ModLoader.Forge, instance.Loader);
            Assert.Equal("47.2.0", instance.LoaderVersion);
            Assert.NotNull(instance.GameDirectory);

            var modPath = Path.Combine(instance.GameDirectory!, "mods", "coolmod-1.0.jar");
            Assert.True(File.Exists(modPath), $"Expected mod at {modPath}");
            Assert.Equal(byteSequence, File.ReadAllBytes(modPath));

            var configPath = Path.Combine(instance.GameDirectory!, "config", "cool.toml");
            Assert.True(File.Exists(configPath));
            Assert.Equal("key = 1", File.ReadAllText(configPath));

            var scriptPath = Path.Combine(instance.GameDirectory!, "scripts", "init.lua");
            Assert.True(File.Exists(scriptPath));
        }
        finally { TryDelete(workdir); }
    }

    [Fact]
    public async Task ImportAsync_RespectsExplicitInstanceName()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "pack.zip");
            BuildCurseForgeZip(archivePath, """
                {
                  "minecraft": { "version": "1.20.1", "modLoaders": [{ "id": "fabric-0.14.21", "primary": true }] },
                  "manifestType": "minecraftModpack", "manifestVersion": 1,
                  "name": "Default", "version": "1", "files": []
                }
                """);

            var stub = new StubRepository(
                listFiles: (_, _, _, _) => Task.FromResult<IReadOnlyList<ModFile>>(Array.Empty<ModFile>()),
                download: (_, _, _, _) => Task.CompletedTask);

            var importer = new CurseForgeModpackImporter(Path.Combine(workdir, "instances"), stub);
            var inst = await importer.ImportAsync(archivePath, "Renamed", null, CancellationToken.None);
            Assert.Equal("Renamed", inst.Name);
            Assert.Equal(ModLoader.Fabric, inst.Loader);
            Assert.Equal("0.14.21", inst.LoaderVersion);
        }
        finally { TryDelete(workdir); }
    }

    [Fact]
    public void ParseLoaderId_HandlesAllKnownPrefixes()
    {
        Assert.Equal((ModLoader.Forge, (string?)"47.2.0"), CurseForgeModpackImporter.ParseLoaderId("forge-47.2.0"));
        Assert.Equal((ModLoader.Fabric, (string?)"0.14.21"), CurseForgeModpackImporter.ParseLoaderId("fabric-0.14.21"));
        Assert.Equal((ModLoader.NeoForge, (string?)"20.4.50"), CurseForgeModpackImporter.ParseLoaderId("neoforge-20.4.50"));
        Assert.Equal((ModLoader.Quilt, (string?)"0.21.0"), CurseForgeModpackImporter.ParseLoaderId("quilt-0.21.0"));
        Assert.Equal((ModLoader.None, (string?)null), CurseForgeModpackImporter.ParseLoaderId("unknownloader-1.0"));
    }

    [Fact]
    public void ParseManifest_ExtractsProjectIdFilePairs()
    {
        using var ms = new MemoryStream();
        BuildCurseForgeZipTo(ms, """
            {
              "minecraft": { "version": "1.20.1", "modLoaders": [{ "id": "fabric-1", "primary": true }] },
              "manifestType": "minecraftModpack", "manifestVersion": 1,
              "name": "p", "version": "1",
              "files": [
                { "projectID": 11, "fileID": 22, "required": true },
                { "projectID": 33, "fileID": 44, "required": false }
              ]
            }
            """);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var (_, fileRefs) = CurseForgeModpackImporter.ParseManifest(zip);
        Assert.Equal(2, fileRefs.Count);
        Assert.Equal(11, fileRefs[0].ProjectId);
        Assert.Equal(22, fileRefs[0].FileId);
        Assert.True(fileRefs[0].Required);
        Assert.False(fileRefs[1].Required);
    }

    [Fact]
    public async Task ImportAsync_ThrowsOnMissingFileResolution()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "pack.zip");
            BuildCurseForgeZip(archivePath, """
                {
                  "minecraft": { "version": "1.20.1", "modLoaders": [{ "id": "forge-1", "primary": true }] },
                  "manifestType": "minecraftModpack", "manifestVersion": 1,
                  "name": "p", "version": "1",
                  "files": [{ "projectID": 1, "fileID": 99, "required": true }]
                }
                """);
            var stub = new StubRepository(
                listFiles: (_, _, _, _) => Task.FromResult<IReadOnlyList<ModFile>>(Array.Empty<ModFile>()),
                download: (_, _, _, _) => Task.CompletedTask);
            var importer = new CurseForgeModpackImporter(Path.Combine(workdir, "instances"), stub);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                importer.ImportAsync(archivePath, null, null, CancellationToken.None));
        }
        finally { TryDelete(workdir); }
    }

    // ---------- helpers ----------

    private static void BuildCurseForgeZip(string path, string manifestJson, params (string Path, string Content)[] overrides)
    {
        using var fs = File.Create(path);
        BuildCurseForgeZipTo(fs, manifestJson, overrides);
    }

    private static void BuildCurseForgeZipTo(Stream dest, string manifestJson, params (string Path, string Content)[] overrides)
    {
        using var zip = new ZipArchive(dest, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = zip.CreateEntry("manifest.json");
        using (var w = new StreamWriter(manifest.Open(), Encoding.UTF8)) w.Write(manifestJson);
        foreach (var (p, c) in overrides)
        {
            var e = zip.CreateEntry(p);
            using var w = new StreamWriter(e.Open(), Encoding.UTF8);
            w.Write(c);
        }
    }

    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "hyperion-cfmodpack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private sealed class StubRepository : IModRepository
    {
        private readonly Func<string, string?, ModLoader?, CancellationToken, Task<IReadOnlyList<ModFile>>> _listFiles;
        private readonly Func<ModFile, Stream, IProgress<double>?, CancellationToken, Task> _download;

        public StubRepository(
            Func<string, string?, ModLoader?, CancellationToken, Task<IReadOnlyList<ModFile>>> listFiles,
            Func<ModFile, Stream, IProgress<double>?, CancellationToken, Task> download)
        {
            _listFiles = listFiles;
            _download = download;
        }

        public ModSource Source => ModSource.CurseForge;

        public Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        public Task<IReadOnlyList<ModFile>> ListFilesAsync(string modId, string? gameVersion, ModLoader? loader, CancellationToken cancellationToken)
            => _listFiles(modId, gameVersion, loader, cancellationToken);

        public Task DownloadAsync(ModFile file, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
            => _download(file, destination, progress, cancellationToken);
    }
}
