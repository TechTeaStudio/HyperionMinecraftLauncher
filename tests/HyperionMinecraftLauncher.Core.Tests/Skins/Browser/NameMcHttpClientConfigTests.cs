using System.Linq;
using System.Net;
using System.Net.Http;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

// v0.32.3: NameMcSkinBrowser is [Obsolete]. The header tests are still relevant -
// they pin Chrome 124 fingerprint shape so any future namemc.com revival from a
// proxy / server-side helper inherits the work F2 did.
#pragma warning disable CS0618

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Header-config tests for <see cref="NameMcSkinBrowser.ConfigureHttpClient(HttpClient)"/>.
/// NameMC's Cloudflare layer 403s any request whose fingerprint differs from a real
/// browser. Each test pins one piece of the Chrome 124 fingerprint so a regression
/// here surfaces in CI instead of a silent 403 in production.
/// </summary>
public class NameMcHttpClientConfigTests
{
    private static HttpClient Configured()
    {
        var http = new HttpClient();
        NameMcSkinBrowser.ConfigureHttpClient(http);
        return http;
    }

    [Fact]
    public void UserAgent_StartsWithMozilla()
    {
        using var http = Configured();
        var ua = string.Join(' ', http.DefaultRequestHeaders.UserAgent.Select(p => p.ToString()));
        Assert.StartsWith("Mozilla/", ua);
    }

    [Fact]
    public void UserAgent_DoesNotMentionLauncherName()
    {
        // The whole point of the rewrite: Cloudflare flagged the launcher-branded UA.
        // If someone re-introduces the branded UA the gallery 403s again.
        using var http = Configured();
        var ua = string.Join(' ', http.DefaultRequestHeaders.UserAgent.Select(p => p.ToString()));
        Assert.DoesNotContain("HyperionMinecraftLauncher", ua);
    }

    [Fact]
    public void AcceptLanguage_IsPresent()
    {
        using var http = Configured();
        Assert.NotEmpty(http.DefaultRequestHeaders.AcceptLanguage);
    }

    [Fact]
    public void AcceptEncoding_AdvertisesBrotliGzipDeflate()
    {
        using var http = Configured();
        var ae = string.Join(", ", http.DefaultRequestHeaders.AcceptEncoding.Select(p => p.Value));
        Assert.Contains("gzip", ae);
        Assert.Contains("deflate", ae);
        Assert.Contains("br", ae);
    }

    [Fact]
    public void Referer_IsNameMcOrigin()
    {
        using var http = Configured();
        Assert.True(http.DefaultRequestHeaders.TryGetValues("Referer", out var values));
        Assert.Equal("https://namemc.com/", values!.Single());
    }

    [Fact]
    public void SecFetchHeaders_MatchTopLevelDocumentNavigation()
    {
        using var http = Configured();
        AssertHeader(http, "Sec-Fetch-Dest", "document");
        AssertHeader(http, "Sec-Fetch-Mode", "navigate");
        AssertHeader(http, "Sec-Fetch-Site", "none");
        AssertHeader(http, "Sec-Fetch-User", "?1");
    }

    [Fact]
    public void UpgradeInsecureRequests_IsOne()
    {
        using var http = Configured();
        AssertHeader(http, "Upgrade-Insecure-Requests", "1");
    }

    [Fact]
    public void CacheControl_NoCache_AndPragma_NoCache()
    {
        using var http = Configured();
        AssertHeader(http, "Cache-Control", "no-cache");
        AssertHeader(http, "Pragma", "no-cache");
    }

    [Fact]
    public void DefaultRequestVersion_IsHttp2()
    {
        using var http = Configured();
        Assert.Equal(HttpVersion.Version20, http.DefaultRequestVersion);
    }

    [Fact]
    public void Configure_IsIdempotent_DoesNotDuplicateHeaders()
    {
        // First call sets the full header set. A second call must be a no-op:
        // the same header values, no duplication.
        using var http = new HttpClient();
        NameMcSkinBrowser.ConfigureHttpClient(http);
        var uaTokensAfterFirst = http.DefaultRequestHeaders.UserAgent.Count;
        NameMcSkinBrowser.ConfigureHttpClient(http);

        // Each single-valued header should still be present exactly once.
        Assert.True(http.DefaultRequestHeaders.TryGetValues("Referer", out var ref1));
        Assert.Single(ref1!);
        Assert.True(http.DefaultRequestHeaders.TryGetValues("Sec-Fetch-Dest", out var sfd));
        Assert.Single(sfd!);
        // The UA is multi-token (Mozilla, AppleWebKit, ...) but the token count must not grow.
        Assert.Equal(uaTokensAfterFirst, http.DefaultRequestHeaders.UserAgent.Count);
    }

    [Fact]
    public void Configure_PreservesCallerSuppliedUserAgent()
    {
        // The App layer should be able to pre-stamp a different UA (e.g. for tests
        // or a custom build) and have ConfigureHttpClient leave it alone.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FixtureUA/1.0");
        NameMcSkinBrowser.ConfigureHttpClient(http);

        var ua = string.Join(' ', http.DefaultRequestHeaders.UserAgent.Select(p => p.ToString()));
        Assert.Contains("FixtureUA/1.0", ua);
    }

    [Fact]
    public void Constructor_AppliesHeadersAutomatically()
    {
        // The browser's ctor must call ConfigureHttpClient itself, so callers that
        // hand in a fresh HttpClient (e.g. the parser tests) inherit the full
        // browser-like header set without having to remember to wire it.
        using var http = new HttpClient();
        _ = new NameMcSkinBrowser(http);

        Assert.NotEmpty(http.DefaultRequestHeaders.UserAgent);
        Assert.True(http.DefaultRequestHeaders.TryGetValues("Referer", out _));
    }

    private static void AssertHeader(HttpClient http, string name, string expected)
    {
        Assert.True(
            http.DefaultRequestHeaders.TryGetValues(name, out var values),
            "missing header " + name);
        Assert.Equal(expected, values!.Single());
    }
}
