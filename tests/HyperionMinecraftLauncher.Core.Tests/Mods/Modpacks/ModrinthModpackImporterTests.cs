using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
            var modBytes = Encoding.UTF8.GetBytes("MOD-BYTES-XX"); // 12 bytes
            BuildMrpack(archivePath,
                manifestJson: $$"""
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "versionId": "1.0",
                  "name": "Test Pack",
                  "files": [
                    {
                      "path": "mods/example-1.0.jar",
                      "hashes": { "sha1": "{{Sha1(modBytes)}}" },
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

    [Fact]
    public async Task ImportAsync_Sha1Mismatch_Throws_AndRemovesPartialInstance()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "bad.mrpack");
            BuildMrpack(archivePath, manifestJson: """
                {
                  "name": "P", "versionId": "0", "dependencies": { "minecraft": "1.20.1" },
                  "files": [ { "path": "mods/a.jar", "hashes": { "sha1": "0000000000000000000000000000000000000000" },
                               "downloads": ["https://cdn.example/a.jar"] } ]
                }
                """);
            using var http = new HttpClient(new StubHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")) }));
            var instancesRoot = Path.Combine(workdir, "instances");
            var importer = new ModrinthModpackImporter(instancesRoot, http);

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                importer.ImportAsync(archivePath, null, null, CancellationToken.None));

            Assert.Contains("mods/a.jar", ex.Message);
            Assert.False(Directory.Exists(instancesRoot) && Directory.EnumerateFileSystemEntries(instancesRoot).Any(),
                "a failed import must not leave a half-populated instance folder behind");
        }
        finally { TryDelete(workdir); }
    }

    [Fact]
    public async Task ImportAsync_FirstMirrorFails_FallsBackToNextDownloadUrl()
    {
        var workdir = NewTempDir();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("JAR");
            var archivePath = Path.Combine(workdir, "mirrors.mrpack");
            BuildMrpack(archivePath, manifestJson: $$"""
                {
                  "name": "P", "versionId": "0", "dependencies": { "minecraft": "1.20.1" },
                  "files": [ { "path": "mods/a.jar", "hashes": { "sha1": "{{Sha1(bytes)}}" },
                               "downloads": ["https://dead.example/a.jar", "https://cdn.example/a.jar"] } ]
                }
                """);
            using var http = new HttpClient(new StubHandler((req, _) => req.RequestUri!.Host == "dead.example"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            var importer = new ModrinthModpackImporter(Path.Combine(workdir, "instances"), http);

            var inst = await importer.ImportAsync(archivePath, null, null, CancellationToken.None);

            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(inst.GameDirectory!, "mods", "a.jar")));
        }
        finally { TryDelete(workdir); }
    }

    [Fact]
    public async Task ImportAsync_ManyFiles_AllDownloadedWithProgressReachingOne()
    {
        var workdir = NewTempDir();
        try
        {
            var files = string.Join(",", Enumerable.Range(0, 12).Select(i =>
                $$"""{ "path": "mods/m{{i}}.jar", "hashes": { "sha1": "{{Sha1(Encoding.UTF8.GetBytes($"m{i}"))}}" }, "downloads": ["https://cdn.example/m{{i}}.jar"] }"""));
            var archivePath = Path.Combine(workdir, "many.mrpack");
            BuildMrpack(archivePath, manifestJson: $$"""
                { "name": "P", "versionId": "0", "dependencies": { "minecraft": "1.20.1" }, "files": [ {{files}} ] }
                """);
            using var http = new HttpClient(new StubHandler((req, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(req.RequestUri!.AbsolutePath))),
            }));
            var importer = new ModrinthModpackImporter(Path.Combine(workdir, "instances"), http);
            var reports = new System.Collections.Concurrent.ConcurrentBag<double>();

            var inst = await importer.ImportAsync(archivePath, null, new SyncProgress(reports.Add), CancellationToken.None);

            for (var i = 0; i < 12; i++)
                Assert.Equal($"m{i}", File.ReadAllText(Path.Combine(inst.GameDirectory!, "mods", $"m{i}.jar")));
            Assert.Equal(1.0, reports.Max());
        }
        finally { TryDelete(workdir); }
    }

    [Theory]
    [InlineData("../evil.jar")]
    [InlineData("mods/../../evil.jar")]
    [InlineData("C:/Windows/evil.jar")]
    [InlineData("/etc/evil.jar")]
    public void ParseManifest_PathEscapingInstance_IsRejected(string path)
    {
        using var ms = new MemoryStream();
        BuildMrpackTo(ms, manifestJson: $$"""
            { "name": "P", "versionId": "0", "dependencies": { "minecraft": "1.20.1" },
              "files": [ { "path": "{{path}}", "downloads": ["https://cdn.example/x"] } ] }
            """);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() => ModrinthModpackImporter.ParseManifest(zip));
    }

    [Fact]
    public async Task ImportAsync_OverrideEscapingInstance_IsRejected()
    {
        var workdir = NewTempDir();
        try
        {
            var archivePath = Path.Combine(workdir, "slip.mrpack");
            BuildMrpack(archivePath,
                manifestJson: """{ "name": "P", "versionId": "0", "dependencies": { "minecraft": "1.20.1" }, "files": [] }""",
                overrides: new[] { ("overrides/../../escaped.txt", "pwned") });
            using var http = new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("no downloads")));
            var importer = new ModrinthModpackImporter(Path.Combine(workdir, "instances"), http);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                importer.ImportAsync(archivePath, null, null, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(workdir, "escaped.txt")));
        }
        finally { TryDelete(workdir); }
    }

    // ---------- helpers ----------

    private static string Sha1(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this one reports inline so asserts see every tick.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

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
