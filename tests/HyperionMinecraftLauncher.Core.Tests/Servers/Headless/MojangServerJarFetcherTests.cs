using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers.Headless;

/// <summary>
/// Covers <see cref="MojangServerJarFetcher"/> with a stubbed <see cref="HttpMessageHandler"/>.
/// Manifest -> per-version metadata -> jar download chain is faked so the tests stay offline
/// and deterministic. Each test owns a fresh temp directory.
/// </summary>
public sealed class MojangServerJarFetcherTests : IDisposable
{
    private readonly string _tempDir;

    public MojangServerJarFetcherTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "HMLTests_JarFetcher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task EnsureServerJarAsync_DownloadsJarAndVerifiesSha1()
    {
        // Arrange: fake jar bytes + their real sha1.
        var jarBytes = Encoding.UTF8.GetBytes("FAKE_SERVER_JAR_PAYLOAD_v1");
        var sha1 = Convert.ToHexString(SHA1.HashData(jarBytes)).ToLowerInvariant();

        const string manifestUrl = "https://stub.example/manifest.json";
        const string versionMetaUrl = "https://piston-meta.example/v1/packages/abc/1.21.5.json";
        const string serverJarUrl = "https://piston-data.example/server.jar";

        var manifestJson =
            "{\"versions\":[" +
            $"{{\"id\":\"1.21.5\",\"url\":\"{versionMetaUrl}\"}}" +
            "]}";
        var versionJson =
            "{\"downloads\":{\"server\":{" +
            $"\"url\":\"{serverJarUrl}\",\"sha1\":\"{sha1}\",\"size\":{jarBytes.Length}" +
            "}}}";

        var handler = new StubHandler();
        handler.Map(manifestUrl, manifestJson, isText: true);
        handler.Map(versionMetaUrl, versionJson, isText: true);
        handler.Map(serverJarUrl, jarBytes);

        var http = new HttpClient(handler);
        var fetcher = new MojangServerJarFetcher(http, manifestUrl);

        // Act
        var path = await fetcher.EnsureServerJarAsync("1.21.5", _tempDir, progress: null, CancellationToken.None);

        // Assert: jar written to target dir + sha1 sidecar present + content matches.
        Assert.Equal(Path.Combine(_tempDir, "server.jar"), path);
        Assert.True(File.Exists(path));
        var contents = await File.ReadAllBytesAsync(path);
        Assert.Equal(jarBytes, contents);

        var sidecar = Path.Combine(_tempDir, "server.jar.sha1");
        Assert.True(File.Exists(sidecar));
        var recorded = (await File.ReadAllTextAsync(sidecar)).Trim();
        Assert.Equal(sha1, recorded, ignoreCase: true);
    }

    [Fact]
    public async Task EnsureServerJarAsync_ExistingJarWithMatchingHash_SkipsDownload()
    {
        // Arrange: pre-seed the jar + sidecar.
        var jarBytes = Encoding.UTF8.GetBytes("CACHED_JAR");
        var sha1 = Convert.ToHexString(SHA1.HashData(jarBytes)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "server.jar"), jarBytes);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "server.jar.sha1"), sha1);

        const string manifestUrl = "https://stub.example/manifest.json";
        const string versionMetaUrl = "https://piston-meta.example/v1/packages/abc/1.21.5.json";

        var manifestJson =
            "{\"versions\":[" +
            $"{{\"id\":\"1.21.5\",\"url\":\"{versionMetaUrl}\"}}" +
            "]}";
        var versionJson =
            "{\"downloads\":{\"server\":{" +
            $"\"url\":\"https://piston-data.example/server.jar\",\"sha1\":\"{sha1}\",\"size\":{jarBytes.Length}" +
            "}}}";

        var handler = new StubHandler();
        handler.Map(manifestUrl, manifestJson, isText: true);
        handler.Map(versionMetaUrl, versionJson, isText: true);
        // No mapping for the jar url - any request would 404 and the test would fail.

        var http = new HttpClient(handler);
        var fetcher = new MojangServerJarFetcher(http, manifestUrl);

        // Act
        var path = await fetcher.EnsureServerJarAsync("1.21.5", _tempDir, progress: null, CancellationToken.None);

        // Assert: jar untouched, sha1 sidecar unchanged.
        Assert.Equal(Path.Combine(_tempDir, "server.jar"), path);
        var afterBytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(jarBytes, afterBytes);
    }

    [Fact]
    public async Task EnsureServerJarAsync_Sha1Mismatch_DeletesJarAndThrows()
    {
        // Arrange: serve junk that doesn't match the claimed sha1.
        var realBytes = Encoding.UTF8.GetBytes("REAL_BYTES");
        var wrongBytes = Encoding.UTF8.GetBytes("CORRUPT");
        var expectedSha1 = Convert.ToHexString(SHA1.HashData(realBytes)).ToLowerInvariant();

        const string manifestUrl = "https://stub.example/manifest.json";
        const string versionMetaUrl = "https://piston-meta.example/v1/packages/abc/1.21.5.json";
        const string serverJarUrl = "https://piston-data.example/server.jar";

        var manifestJson =
            "{\"versions\":[" +
            $"{{\"id\":\"1.21.5\",\"url\":\"{versionMetaUrl}\"}}" +
            "]}";
        var versionJson =
            "{\"downloads\":{\"server\":{" +
            $"\"url\":\"{serverJarUrl}\",\"sha1\":\"{expectedSha1}\",\"size\":{wrongBytes.Length}" +
            "}}}";

        var handler = new StubHandler();
        handler.Map(manifestUrl, manifestJson, isText: true);
        handler.Map(versionMetaUrl, versionJson, isText: true);
        handler.Map(serverJarUrl, wrongBytes);

        var http = new HttpClient(handler);
        var fetcher = new MojangServerJarFetcher(http, manifestUrl);

        // Act + Assert.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fetcher.EnsureServerJarAsync("1.21.5", _tempDir, progress: null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_tempDir, "server.jar")),
            "Corrupt download must be deleted so a retry actually re-fetches.");
    }

    [Fact]
    public async Task EnsureServerJarAsync_UnknownVersion_Throws()
    {
        const string manifestUrl = "https://stub.example/manifest.json";

        var handler = new StubHandler();
        handler.Map(manifestUrl, "{\"versions\":[]}", isText: true);

        var http = new HttpClient(handler);
        var fetcher = new MojangServerJarFetcher(http, manifestUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fetcher.EnsureServerJarAsync("9.9.9", _tempDir, progress: null, CancellationToken.None));
    }

    /// <summary>
    /// Minimal HTTP handler: each registered url returns the same bytes verbatim. Anything
    /// unregistered comes back 404 so a missing mapping fails loud instead of hitting the real network.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly System.Collections.Generic.Dictionary<string, (byte[] Bytes, bool IsText)> _routes = new();

        public void Map(string url, string body, bool isText) =>
            _routes[url] = (Encoding.UTF8.GetBytes(body), isText);

        public void Map(string url, byte[] body) =>
            _routes[url] = (body, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (!_routes.TryGetValue(url, out var entry))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>()),
                });
            }

            var content = new ByteArrayContent(entry.Bytes);
            if (entry.IsText)
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            else
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/java-archive");
            content.Headers.ContentLength = entry.Bytes.Length;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
