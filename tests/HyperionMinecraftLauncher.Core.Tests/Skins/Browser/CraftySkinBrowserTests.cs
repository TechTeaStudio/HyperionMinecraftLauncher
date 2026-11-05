using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Tests for <see cref="CraftySkinBrowser"/>, the crafty.gg-backed skin catalog. A stub
/// handler serves recorded-shape JSON so no network traffic happens.
/// </summary>
public class CraftySkinBrowserTests
{
    private const string TrendingJson = /*lang=json,strict*/ """
    {
      "success": true,
      "meta": { "page": 1, "limit": 10, "days": 7, "sort": "increase", "total": 10, "hasMore": true },
      "data": [
        { "id": "f63b6ccb-61b2-4544-b879-f396e54ed72c", "hash": "fc583cf0aba7568155039f46e686703c16f44a293473d329a3b6b6cb4b131b94",
          "slim": true, "banned": false, "deleted_at": null, "upvotes_lifetime": 7, "players_count": 25450,
          "first_player": { "id": "e965c174", "username": "leonardo1909" } },
        { "id": "0ae49fd5-0601-4f04-967b-f044ae0c87f8", "hash": "b95939e55aa93c4ae44c4e631174e7664792d354fe171995627213aedcca0fa3",
          "slim": false, "banned": true, "deleted_at": null, "upvotes_lifetime": 1, "first_player": null },
        { "id": "c6daf563-15a8-416a-8b35-d3a544762e7c", "hash": "afdd6e3e9059aa66582c79e48c27a73d654f36fdbf583d4298ff679cf2010b5b",
          "slim": false, "banned": false, "deleted_at": "2026-10-06T10:00:51.210+02:00", "upvotes_lifetime": 0, "first_player": null },
        { "id": "11111111-2222-3333-4444-555555555555", "hash": "2222222222222222222222222222222222222222222222222222222222222222",
          "slim": false, "banned": false, "deleted_at": null, "upvotes_lifetime": 0, "first_player": null }
      ]
    }
    """;

    private const string PlayerJson = /*lang=json,strict*/ """
    {
      "success": true,
      "data": {
        "uuid": "1ff59ff9e67547719d2efb2ab06093ca",
        "username": "The_LL",
        "skins": [
          { "id": "4f9a8d4e-f739-4659-b01d-6678008d016e", "hash": "25253a4922b0d79ffa84809dacc4b5d24d3c0bd15dbbb7eed78d1e42fc2fe34b",
            "slim": false, "banned": false, "deleted_at": null, "hidden": false, "upvotes_lifetime": 2 },
          { "id": "8d19ce95-1120-4c9d-9881-a6099617ec0a", "hash": "525ba1a31a1bad069f2613a594caa7d5ad3e4113c2295aed25f07282d5a5fb84",
            "slim": true, "banned": false, "deleted_at": null, "hidden": true, "upvotes_lifetime": 0 },
          { "id": "6874909c-a4c1-46e6-901f-989d08d4d565", "hash": "4fe8e6d33e82fb209a1d6adecfd7b001500c63c03bfc3393e353bd053f35246e",
            "slim": false, "banned": false, "deleted_at": null, "hidden": false, "upvotes_lifetime": 0 }
        ]
      }
    }
    """;

    [Fact]
    public void ParseSkinList_SkipsBannedAndDeletedSkins()
    {
        var skins = CraftySkinBrowser.ParseSkinList(TrendingJson);

        Assert.Equal(new[] { "f63b6ccb-61b2-4544-b879-f396e54ed72c", "11111111-2222-3333-4444-555555555555" }, skins.Select(s => s.Id));
    }

    [Fact]
    public void ParseSkinList_MapsTextureVariantUploaderAndPage()
    {
        var skin = CraftySkinBrowser.ParseSkinList(TrendingJson)[0];

        Assert.Equal("https://textures.minecraft.net/texture/fc583cf0aba7568155039f46e686703c16f44a293473d329a3b6b6cb4b131b94", skin.PngDownloadUrl);
        Assert.Equal(skin.PngDownloadUrl, skin.ThumbnailUrl);
        Assert.Equal("https://crafty.gg/skins/f63b6ccb-61b2-4544-b879-f396e54ed72c", skin.SourceUrl);
        Assert.Equal(SkinVariant.Slim, skin.Variant);
        Assert.Equal("leonardo1909", skin.UploaderName);
        Assert.Equal(7, skin.Likes);
        Assert.False(skin.IsLivePlayerSkin);
    }

    [Fact]
    public async Task ListTrendingAsync_FetchesTheCraftyPagesBehindOneCatalogPage()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, TrendingJson));
        using var http = new HttpClient(handler);
        var browser = new CraftySkinBrowser(http);

        var result = await browser.ListTrendingAsync(page: 1, limit: 30, CancellationToken.None);

        // Crafty serves 10 per page: catalog page 1 of 30 is crafty pages 4, 5 and 6.
        Assert.Equal(new[] { 4, 5, 6 }, handler.Urls.Select(PageOf).OrderBy(p => p));
        Assert.All(handler.Urls, u => Assert.StartsWith("https://api.crafty.gg/api/v2/skins/trending", u));
        // The stub serves the same two visible skins on every page; repeats across pages
        // (trending ranks shift between requests) are dropped.
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchAsync_ListsThePlayersVisibleSkinsNewestFirst()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, PlayerJson));
        using var http = new HttpClient(handler);
        var browser = new CraftySkinBrowser(http);

        var result = await browser.SearchAsync("The_LL", page: 0, limit: 60, CancellationToken.None);

        Assert.Equal("https://api.crafty.gg/api/v2/players/The_LL", Assert.Single(handler.Urls));
        Assert.Equal(new[] { "4f9a8d4e-f739-4659-b01d-6678008d016e", "6874909c-a4c1-46e6-901f-989d08d4d565" }, result.Select(s => s.Id));
        Assert.True(result[0].IsLivePlayerSkin);
        Assert.False(result[1].IsLivePlayerSkin);
        Assert.All(result, s => Assert.Equal("The_LL", s.UploaderName));
    }

    [Fact]
    public async Task SearchAsync_UnknownPlayerOrBadNickname_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.NotFound, """{"success":false}"""));
        using var http = new HttpClient(handler);
        var browser = new CraftySkinBrowser(http);

        Assert.Empty(await browser.SearchAsync("NoSuchPlayer123", 0, 60, CancellationToken.None));
        Assert.Empty(await browser.SearchAsync("not a nickname!", 0, 60, CancellationToken.None));
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task SearchAsync_LaterPages_AreEmpty()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, PlayerJson));
        using var http = new HttpClient(handler);
        var browser = new CraftySkinBrowser(http);

        Assert.Empty(await browser.SearchAsync("The_LL", page: 1, limit: 60, CancellationToken.None));
    }

    private static int PageOf(string url)
    {
        var query = new Uri(url).Query.TrimStart('?').Split('&');
        return int.Parse(query.First(p => p.StartsWith("page=", StringComparison.Ordinal))["page=".Length..]);
    }

    private sealed class StubHandler(Func<string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (_gate) Urls.Add(url);
            var (status, body) = respond(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
