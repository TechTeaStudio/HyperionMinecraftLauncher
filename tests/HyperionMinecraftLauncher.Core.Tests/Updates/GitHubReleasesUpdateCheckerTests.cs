using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Updates;

public class GitHubReleasesUpdateCheckerTests : IDisposable
{
    private readonly string _cacheDir;

    public GitHubReleasesUpdateCheckerTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "hyperion-updates-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CheckAsync_NewerRelease_ReturnsUpdateInfo()
    {
        const string sampleJson = """
        {
          "tag_name": "v0.99.0",
          "html_url": "https://github.com/TechTeaStudio/HyperionMinecraftLauncher/releases/tag/v0.99.0",
          "published_at": "2026-05-01T12:34:56Z",
          "body": "Bug fixes and a sparkly new feature."
        }
        """;
        var handler = new StubHandler((req, ct) =>
        {
            Assert.Contains("TechTeaStudio/HyperionMinecraftLauncher", req.RequestUri!.ToString());
            Assert.NotNull(req.Headers.UserAgent);
            Assert.NotEmpty(req.Headers.UserAgent.ToString()!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sampleJson),
            });
        });
        using var http = new HttpClient(handler);
        var cache = new FileCache(_cacheDir);
        var checker = new GitHubReleasesUpdateChecker(http, cache);

        var info = await checker.CheckAsync("0.28.0", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("0.99.0", info!.LatestVersion);
        Assert.Equal("https://github.com/TechTeaStudio/HyperionMinecraftLauncher/releases/tag/v0.99.0", info.ReleaseUrl);
        Assert.Equal("Bug fixes and a sparkly new feature.", info.Notes);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 34, 56, TimeSpan.Zero), info.PublishedAt);
    }

    [Fact]
    public async Task CheckAsync_SameVersion_ReturnsNull()
    {
        const string sampleJson = """
        {
          "tag_name": "v0.28.0",
          "html_url": "https://github.com/TechTeaStudio/HyperionMinecraftLauncher/releases/tag/v0.28.0",
          "published_at": "2026-05-01T00:00:00Z",
          "body": ""
        }
        """;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sampleJson),
        }));
        using var http = new HttpClient(handler);
        var checker = new GitHubReleasesUpdateChecker(http, new FileCache(_cacheDir));

        var info = await checker.CheckAsync("0.28.0", CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task CheckAsync_HttpNotFound_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found"),
        }));
        using var http = new HttpClient(handler);
        var checker = new GitHubReleasesUpdateChecker(http, new FileCache(_cacheDir));

        var info = await checker.CheckAsync("0.28.0", CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task CheckAsync_OlderRemoteVersion_ReturnsNull()
    {
        const string sampleJson = """
        { "tag_name": "v0.10.0", "html_url": "https://x/y", "published_at": "2020-01-01T00:00:00Z", "body": "" }
        """;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sampleJson),
        }));
        using var http = new HttpClient(handler);
        var checker = new GitHubReleasesUpdateChecker(http, new FileCache(_cacheDir));

        var info = await checker.CheckAsync("0.28.0", CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task CheckAsync_TransportException_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("simulated network down"));
        using var http = new HttpClient(handler);
        var checker = new GitHubReleasesUpdateChecker(http, new FileCache(_cacheDir));

        var info = await checker.CheckAsync("0.28.0", CancellationToken.None);

        Assert.Null(info);
    }

    /// <summary>Inline HttpMessageHandler that delegates each call to a user-supplied lambda.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
