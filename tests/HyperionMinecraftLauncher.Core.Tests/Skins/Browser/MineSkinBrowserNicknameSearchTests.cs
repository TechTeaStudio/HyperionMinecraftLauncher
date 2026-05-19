using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// I1 (v0.32.5): exercise the hybrid Mojang-username -> live-player-skin path on
/// <see cref="MineSkinBrowser.SearchAsync(string, int, int, CancellationToken)"/>.
/// All tests stub <see cref="HttpMessageHandler"/> so no real network traffic is generated.
/// </summary>
public class MineSkinBrowserNicknameSearchTests
{
    /// <summary>Notch's real UUID. Used as the well-known fixture across these tests.</summary>
    private const string NotchUuid = "069a79f444e94726a5befca90e38aaf5";

    /// <summary>Empty MineSkin response so the merged list contains only the Mojang entry.</summary>
    private const string EmptyMineSkinJson = /*lang=json,strict*/ """
    { "success": true, "skins": [] }
    """;

    /// <summary>Mojang username -> UUID lookup body for "Notch".</summary>
    private const string NotchLookupJson = /*lang=json,strict*/ """
    { "id": "069a79f444e94726a5befca90e38aaf5", "name": "Notch" }
    """;

    /// <summary>
    /// Build a base64-encoded "textures" property value that looks exactly like what
    /// Mojang's session server returns. The inner JSON has a fixed SKIN url; the
    /// optional <paramref name="slim"/> flag toggles the "slim" arm-width model.
    /// </summary>
    private static string MakeTexturesProperty(string skinUrl, bool slim)
    {
        object metadata = slim ? new { model = "slim" } : new object();
        var inner = JsonSerializer.Serialize(new
        {
            timestamp = 0L,
            profileId = NotchUuid,
            profileName = "Notch",
            textures = new Dictionary<string, object>
            {
                ["SKIN"] = slim
                    ? new { url = skinUrl, metadata = new { model = "slim" } }
                    : new { url = skinUrl } as object,
            },
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    }

    /// <summary>Profile response with the textures property pre-encoded.</summary>
    private static string MakeProfileJson(string skinUrl, bool slim = false)
    {
        return JsonSerializer.Serialize(new
        {
            id = NotchUuid,
            name = "Notch",
            properties = new[]
            {
                new { name = "textures", value = MakeTexturesProperty(skinUrl, slim) },
            },
        });
    }

    [Fact]
    public async Task SearchAsync_ValidUsername_PrependsMojangLivePlayerSkin()
    {
        const string skinUrl = "https://textures.minecraft.net/texture/abc123notchskin";
        var handler = new StubHandler();
        handler.Map(MineSkinBrowser.MojangUsernameLookupStem + "Notch", NotchLookupJson);
        handler.Map(MineSkinBrowser.MojangSessionProfileStem + NotchUuid, MakeProfileJson(skinUrl));
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        var result = await browser.SearchAsync("Notch", page: 0, limit: 10, CancellationToken.None);

        Assert.NotEmpty(result);
        var head = result[0];
        Assert.True(head.IsLivePlayerSkin);
        Assert.Equal("Notch", head.UploaderName);
        Assert.Equal(skinUrl, head.PngDownloadUrl);
        Assert.Equal("mojang:" + NotchUuid, head.Id);
    }

    [Fact]
    public async Task SearchAsync_SlimModel_SetsVariantSlim()
    {
        const string skinUrl = "https://textures.minecraft.net/texture/slim-arms";
        var handler = new StubHandler();
        handler.Map(MineSkinBrowser.MojangUsernameLookupStem + "AlexUser", NotchLookupJson);
        handler.Map(MineSkinBrowser.MojangSessionProfileStem + NotchUuid, MakeProfileJson(skinUrl, slim: true));
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        var result = await browser.SearchAsync("AlexUser", page: 0, limit: 10, CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Equal(SkinVariant.Slim, result[0].Variant);
        Assert.True(result[0].IsLivePlayerSkin);
    }

    [Fact]
    public async Task SearchAsync_InvalidUsernameShape_DoesNotCallMojang()
    {
        var handler = new StubHandler();
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        // "space in name" fails the username regex (contains spaces).
        var result = await browser.SearchAsync("space in name", page: 0, limit: 10, CancellationToken.None);

        Assert.Empty(result);
        // Only MineSkin was hit; the Mojang username-lookup endpoint was never touched.
        Assert.All(handler.RequestedUrls, u =>
        {
            Assert.DoesNotContain(MineSkinBrowser.MojangUsernameLookupStem, u, StringComparison.Ordinal);
            Assert.DoesNotContain(MineSkinBrowser.MojangSessionProfileStem, u, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SearchAsync_MojangLookup404_FallsBackToMineSkinOnly()
    {
        var handler = new StubHandler();
        handler.Map(MineSkinBrowser.MojangUsernameLookupStem + "Ghost", body: string.Empty, status: HttpStatusCode.NotFound);
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        // No exception, no Mojang entry, empty MineSkin result -> empty list overall.
        var result = await browser.SearchAsync("Ghost", page: 0, limit: 10, CancellationToken.None);

        Assert.Empty(result);
        // The session-profile call must NOT have happened because the lookup 404'd.
        Assert.DoesNotContain(handler.RequestedUrls, u =>
            u.StartsWith(MineSkinBrowser.MojangSessionProfileStem, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_MojangRateLimited429_GracefulFallback()
    {
        var handler = new StubHandler();
        handler.Map(MineSkinBrowser.MojangUsernameLookupStem + "Steve",
            body: string.Empty, status: (HttpStatusCode)429);
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        // No exception; we just fall through to MineSkin's (empty) gallery filter.
        var result = await browser.SearchAsync("Steve", page: 0, limit: 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_UuidWithDashes_SkipsLookupAndHitsSessionProfile()
    {
        const string dashed = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
        const string skinUrl = "https://textures.minecraft.net/texture/uuid-path";

        var handler = new StubHandler();
        handler.Map(MineSkinBrowser.MojangSessionProfileStem + NotchUuid, MakeProfileJson(skinUrl));
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        var result = await browser.SearchAsync(dashed, page: 0, limit: 10, CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.True(result[0].IsLivePlayerSkin);
        Assert.Equal(skinUrl, result[0].PngDownloadUrl);
        // The username lookup must NOT have been called - the input was already a UUID.
        Assert.DoesNotContain(handler.RequestedUrls, u =>
            u.StartsWith(MineSkinBrowser.MojangUsernameLookupStem, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_NoMojangFallbackWired_BehavesLikeLegacyClientSideFilter()
    {
        var handler = new StubHandler();
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        // No mojangFallback - legacy ctor path.
        var browser = new MineSkinBrowser(http);

        var result = await browser.SearchAsync("Notch", page: 0, limit: 10, CancellationToken.None);

        Assert.Empty(result);
        // No Mojang traffic should ever leave the browser.
        Assert.All(handler.RequestedUrls, u =>
            Assert.DoesNotContain("mojang.com", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_Page1_DoesNotTriggerMojangFallback()
    {
        var handler = new StubHandler();
        handler.MapPrefix(MineSkinBrowser.ListUrlStem, EmptyMineSkinJson);

        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http, mojangFallback: new StubFetcher());

        // Page 1+ is "load more gallery cards", not "first search hit". Mojang fallback
        // is a top-of-search affordance only.
        var result = await browser.SearchAsync("Notch", page: 1, limit: 10, CancellationToken.None);

        Assert.Empty(result);
        Assert.DoesNotContain(handler.RequestedUrls, u =>
            u.StartsWith(MineSkinBrowser.MojangUsernameLookupStem, StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="IPlayerSkinFetcher"/> implementation used as the "capability flag" in the
    /// hybrid resolver. It's never called by the resolver itself - the resolver makes HTTP
    /// calls inline through the browser's shared HttpClient - but the ctor needs a non-null
    /// value to enable the fallback path.
    /// </summary>
    private sealed class StubFetcher : IPlayerSkinFetcher
    {
        public Task<PlayerSkinInfo?> FetchAsync(string uuid, CancellationToken cancellationToken)
            => Task.FromResult<PlayerSkinInfo?>(null);
    }

    /// <summary>
    /// Tiny URL-routed stub. Map exact URLs to canned response bodies; falls back to a
    /// configurable per-stem default for the MineSkin gallery endpoint.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _exact =
            new(StringComparer.Ordinal);
        private readonly List<(string Prefix, HttpStatusCode Status, string Body)> _prefixes = new();
        public List<string> RequestedUrls { get; } = new();

        public void Map(string url, string body, HttpStatusCode status = HttpStatusCode.OK)
            => _exact[url] = (status, body);

        public void MapPrefix(string prefix, string body, HttpStatusCode status = HttpStatusCode.OK)
            => _prefixes.Add((prefix, status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            if (_exact.TryGetValue(url, out var exact))
                return Task.FromResult(new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body ?? string.Empty),
                });

            foreach (var (prefix, status, body) in _prefixes)
            {
                if (url.StartsWith(prefix, StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body),
                    });
            }

            // Unmapped URL: surface as a 404 so the resolver's "no hit" branch exercises.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(string.Empty),
            });
        }
    }
}
