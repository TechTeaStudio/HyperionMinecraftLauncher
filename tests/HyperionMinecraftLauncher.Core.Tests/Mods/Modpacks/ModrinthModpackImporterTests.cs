using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods.Modpacks;

public class ModrinthModpackImporterTests
{
    [Fact]
    public async Task ImportAsync_BuildsInstanceFromMrpack_DownloadsMods_CopiesOverrides()
    {
        // Arrange: synth a .mrpack with one downloadable mod + an overrides/config file.
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "test.mrpack");
            BuildMrpack(archivePath,
                manifestJson: """
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "versionId": "1.0",
                  "name": "Test Pack",
                  "files": [
                    {
                      "path": "mods/example-1.0.jar",
                      "hashes": { "sha1": "deadbeef" },
                      "downloads": ["https://example.com/example-1.0.jar"],
                      "fileSize": 12,
                      "env": { "client": "required", "server": "required" }
                    }
                  ],
                  "dependencies": {
                    "minecraft": "1.20.1",
                    "fabric-loader": "0.14.21"
                  }
                }
                """,
                overrides: new[]
                {
                    ("overrides/config/foo.txt", "hello override")
                });

            var modBytes = Encoding.UTF8.GetBytes("MOD-BYTES-XX"); // 12 bytes
            var handler = new StubHandler((req, _) =>
            {
                Assert.Equal("https://example.com/example-1.0.jar", req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(modBytes),
                };
            });
            using var http = new HttpClient(handler);

            var instancesRoot = Path.Combine(workdir, "instances");
            var importer = new ModrinthModpackImporter(instancesRoot, http);

            // Act
            var instance = await importer.ImportAsync(archivePath, targetInstanceName: null,
                progress: null, cancellationToken: CancellationToken.None);

            // Assert: instance shape
            Assert.Equal("Test Pack", instance.Name);
            Assert.Equal("1.20.1", instance.VersionId);
            Assert.Equal(ModLoader.Fabric, instance.Loader);
            Assert.Equal("0.14.21", instance.LoaderVersion);
            Assert.NotNull(instance.GameDirectory);
            Assert.StartsWith(instancesRoot, instance.GameDirectory);

            // Mod downloaded into the instance dir
            var modPath = Path.Combine(instance.GameDirectory!, "mods", "example-1.0.jar");
            Assert.True(File.Exists(modPath), $"Mod missing at {modPath}");
            Assert.Equal(modBytes, File.ReadAllBytes(modPath));

            // Override copied
            var overridePath = Path.Combine(instance.GameDirectory!, "config", "foo.txt");
            Assert.True(File.Exists(overridePath), $"Override missing at {overridePath}");
            Assert.Equal("hello override", File.ReadAllText(overridePath));
        }
        finally
        {
            TryDelete(workdir);
        }
    }

    [Fact]
    public async Task ImportAsync_UsesProvidedNameOverManifestName()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "test.mrpack");
            BuildMrpack(archivePath,
                manifestJson: """
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "versionId": "1.0",
                  "name": "DefaultName",
                  "files": [],
                  "dependencies": { "minecraft": "1.20.1" }
                }
                """);

            using var http = new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("no files expected")));
            var importer = new ModrinthModpackImporter(Path.Combine(workdir, "instances"), http);

            var inst = await importer.ImportAsync(archivePath, "CustomChoice", null, CancellationToken.None);
            Assert.Equal("CustomChoice", inst.Name);
        }
        finally { TryDelete(workdir); }
    }

    [Fact]
    public void ParseManifest_RecognisesForgeDependency()
    {
        using var ms = new MemoryStream();
        BuildMrpackTo(ms,
            manifestJson: """
            {
              "name": "Forge Pack",
              "versionId": "0.1",
              "files": [],
              "dependencies": { "minecraft": "1.20.1", "forge": "47.2.0" }
            }
            """);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var manifest = ModrinthModpackImporter.ParseManifest(zip);
        Assert.Equal(ModLoader.Forge, manifest.Loader);
        Assert.Equal("47.2.0", manifest.LoaderVersion);
        Assert.Equal("1.20.1", manifest.MinecraftVersion);
        Assert.Empty(manifest.Files);
    }

    [Fact]
    public void ParseManifest_SkipsClientUnsupportedFiles()
    {
        using var ms = new MemoryStream();
        BuildMrpackTo(ms,
            manifestJson: """
            {
              "name": "P", "versionId": "0",
              "dependencies": { "minecraft": "1.20.1" },
              "files": [
                { "path": "mods/server-only.jar", "downloads": ["https://x/y"],
                  "env": { "client": "unsupported", "server": "required" } },
                { "path": "mods/both.jar", "downloads": ["https://x/b"],
                  "env": { "client": "required", "server": "required" } },
                { "path": "mods/optional.jar", "downloads": ["https://x/o"],
                  "env": { "client": "optional", "server": "optional" } }
              ]
            }
            """);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var manifest = ModrinthModpackImporter.ParseManifest(zip);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Contains(manifest.Files, f => f.TargetPath == "mods/both.jar" && f.Required);
        Assert.Contains(manifest.Files, f => f.TargetPath == "mods/optional.jar" && !f.Required);
    }

    [Fact]
    public async Task ImportAsync_ThrowsWhenArchiveMissing()
    {
        using var http = new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));
        var importer = new ModrinthModpackImporter(Path.GetTempPath(), http);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            importer.ImportAsync(Path.Combine(Path.GetTempPath(), "does-not-exist.mrpack"), null, null, CancellationToken.None));
    }

    // ---------- helpers ----------

    private static void BuildMrpack(string path, string manifestJson, params (string Path, string Content)[] overrides)
    {
        using var fs = File.Create(path);
        BuildMrpackTo(fs, manifestJson, overrides);
    }

    private static void BuildMrpackTo(Stream dest, string manifestJson, params (string Path, string Content)[] overrides)
    {
        using var zip = new ZipArchive(dest, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = zip.CreateEntry("modrinth.index.json");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
            writer.Write(manifestJson);

        foreach (var (entryPath, content) in overrides)
        {
            var entry = zip.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "hyperion-modpack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
