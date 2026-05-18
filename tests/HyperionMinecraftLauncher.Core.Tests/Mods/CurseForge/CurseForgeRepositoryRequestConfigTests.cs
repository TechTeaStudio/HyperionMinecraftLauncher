using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods.CurseForge;

/// <summary>
/// v0.32.2 (T-cf-403) regression suite. The user reported 403 Forbidden on every
/// CurseForge search EVEN AFTER pasting a valid key. These tests pin the actual
/// contract: the API key must be attached to each <see cref="HttpRequestMessage"/>
/// in the lowercase <c>x-api-key</c> header, the HttpClient's default headers must
/// stay clean (so no stale value lingers across builds), and non-success responses
/// must surface as friendly hints instead of <c>"403 (Forbidden)"</c>.
/// </summary>
public class CurseForgeRepositoryRequestConfigTests
{
    private const string EmptySearchJson = """{ "data": [] }""";

    [Fact]
    public async Task SearchAsync_AttachesKeyOnRequest_NotOnHttpClientDefaults()
    {
        HttpRequestHeaders? capturedHeaders = null;
        var handler = new CapturingHandler((req, _) =>
        {
            // Snapshot the per-request header collection so the test can assert on it after
            // the response goes through Dispose() in production code.
            capturedHeaders = HttpRequestHeaders.From(req.Headers);
            return Reply(EmptySearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, () => "my-secret-key", new RecordingLogger());

        await repo.SearchAsync(new ModSearchQuery { Query = "sodium" }, CancellationToken.None);

        Assert.NotNull(capturedHeaders);
        Assert.Equal("my-secret-key", capturedHeaders!.Get("x-api-key"));
        // HttpClient defaults must NOT carry the key - the stale-header trap from the bug report.
        Assert.False(http.DefaultRequestHeaders.Contains("x-api-key"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-API-Key"));
    }

    [Fact]
    public void ConstructorScrubsLegacyStaleHeader()
    {
        // Simulate a HttpClient that someone else (e.g. earlier launcher build) poisoned with
        // a default x-api-key. The CurseForgeRepository constructor must remove it so the
        // per-request header is the only writer the wire sees.
        var http = new HttpClient(new CapturingHandler((_, _) => Reply(EmptySearchJson)))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", "STALE-KEY");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", "ALSO-STALE");

        _ = new CurseForgeRepository(http, () => "fresh", new RecordingLogger());

        Assert.False(http.DefaultRequestHeaders.Contains("x-api-key"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-API-Key"));
    }

    [Fact]
    public async Task SearchAsync_TwoConsecutiveCalls_BothUseLatestKey()
    {
        // Two SearchAsync calls on the same repo: each must consult _apiKeyProvider afresh and
        // attach the *current* key to the request. This is the criterion-D test: change the
        // key between calls and assert each request carried its own key.
        var keys = new List<string?>();
        var current = "key-1";
        var handler = new CapturingHandler((req, _) =>
        {
            req.Headers.TryGetValues("x-api-key", out var v);
            keys.Add(v is null ? null : string.Join(",", v));
            return Reply(EmptySearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, () => current, new RecordingLogger());

        await repo.SearchAsync(new ModSearchQuery { Query = "a" }, CancellationToken.None);
        current = "key-2";
        await repo.SearchAsync(new ModSearchQuery { Query = "b" }, CancellationToken.None);
        current = "key-3";
        await repo.SearchAsync(new ModSearchQuery { Query = "c" }, CancellationToken.None);

        Assert.Equal(new[] { "key-1", "key-2", "key-3" }, keys);
    }

    [Fact]
    public async Task SearchAsync_HitsV1EndpointWithGameId432()
    {
        // Pin the URL: most common breakage is silently using /mods/search (no /v1/) or the
        // wrong game id. CurseForge's Minecraft gameId is 432.
        string? capturedUrl = null;
        var handler = new CapturingHandler((req, _) =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return Reply(EmptySearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, () => "k", new RecordingLogger());

        await repo.SearchAsync(new ModSearchQuery { Query = "Sodium" }, CancellationToken.None);

        Assert.NotNull(capturedUrl);
        Assert.StartsWith("https://api.curseforge.com/v1/mods/search?", capturedUrl);
        Assert.Contains("gameId=432", capturedUrl);
        Assert.Contains("searchFilter=Sodium", capturedUrl);
    }

    [Fact]
    public async Task SearchAsync_403_ThrowsFriendlyMessageMentioningKey()
    {
        var http = new HttpClient(new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("forbidden") }))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        var repo = new CurseForgeRepository(http, () => "bad-key", new RecordingLogger());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None));

        Assert.Contains("CurseForge rejected the request", ex.Message);
        Assert.Contains("game id 432", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_401_ThrowsFriendlyMessageAboutInvalidKey()
    {
        var http = new HttpClient(new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        var repo = new CurseForgeRepository(http, () => "expired", new RecordingLogger());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None));

        Assert.Contains("invalid or expired", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_503_ThrowsServiceUnavailableMessage()
    {
        var http = new HttpClient(new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        var repo = new CurseForgeRepository(http, () => "ok", new RecordingLogger());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None));

        Assert.Contains("service is unavailable", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_429_ThrowsRateLimitMessage()
    {
        var http = new HttpClient(new CapturingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        var repo = new CurseForgeRepository(http, () => "ok", new RecordingLogger());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None));

        Assert.Contains("rate-limiting", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_DiagnosticLogIncludesUrlAndRedactedKeyLength()
    {
        // The exact diagnostic line from acceptance criterion E: "[mods] GET <abs> (key length: N)".
        // The key value itself MUST NOT appear in the log line.
        var logger = new RecordingLogger();
        var http = new HttpClient(new CapturingHandler((_, _) => Reply(EmptySearchJson)))
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/"),
        };
        var repo = new CurseForgeRepository(http, () => "secret-abcdef-12345678", logger);

        await repo.SearchAsync(new ModSearchQuery { Query = "Sodium" }, CancellationToken.None);

        var diag = logger.InfoEntries.FirstOrDefault(e => e.StartsWith("[mods] GET"));
        Assert.NotNull(diag);
        Assert.Contains("https://api.curseforge.com/v1/mods/search", diag);
        Assert.Contains("searchFilter=Sodium", diag);
        Assert.Contains("(key length: 21)", diag);
        Assert.DoesNotContain("secret-abcdef", diag);
    }

    private static HttpResponseMessage Reply(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }

    /// <summary>
    /// Immutable snapshot of an <see cref="System.Net.Http.Headers.HttpRequestHeaders"/> so the
    /// test can read values after the request itself has been disposed. The repository disposes
    /// the request before SearchAsync returns - keeping a reference to its Headers property
    /// would observe whatever the GC has done to the underlying store by then.
    /// </summary>
    private sealed class HttpRequestHeaders
    {
        private readonly Dictionary<string, string> _values;
        private HttpRequestHeaders(Dictionary<string, string> values) => _values = values;
        public static HttpRequestHeaders From(System.Net.Http.Headers.HttpRequestHeaders headers)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                d[h.Key] = string.Join(",", h.Value);
            return new HttpRequestHeaders(d);
        }
        public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
    }
}
