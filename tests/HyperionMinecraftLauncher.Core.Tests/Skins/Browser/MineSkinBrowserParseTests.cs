using System.Linq;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Synthetic-fixture tests for <see cref="MineSkinBrowser.ParseList"/>.
/// These never touch the live network: every test feeds a JSON string shaped like
/// a MineSkin v2 <c>/v2/skins</c> response and asserts the parser produces the
/// expected <see cref="BrowsedSkin"/> records.
/// </summary>
public class MineSkinBrowserParseTests
{
    /// <summary>
    /// Real-shape MineSkin v2 response (trimmed to two entries). The structure was captured
    /// from a live <c>curl https://api.mineskin.org/v2/skins?size=2</c> on 2026-05-18; any
    /// schema drift on MineSkin's side will surface here first.
    /// </summary>
    private const string RealShapeJson = /*lang=json,strict*/ """
    {
      "success": true,
      "skins": [
        {
          "uuid": "dd4869016758438b8cb408ab39730caa",
          "shortId": "f3a19bec",
          "name": null,
          "texture": "28f0c73d471fd25c8c527433cd5be82b0a8b0e9b2ae02d0842df72d3597caa54",
          "timestamp": 1779145816855
        },
        {
          "uuid": "7090e39fde664229b2097dc184401f6e",
          "shortId": "936cc382",
          "name": "archmc-fd97569",
          "texture": "6833d8fd032f250cd3daab0ff913626e1d4e973ae8e9bb10a73a91e397f2bc08",
          "timestamp": 1779145794767
        }
      ],
      "pagination": {"current": {}, "next": {"after": "7090e39fde664229b2097dc184401f6e"}},
      "warnings": [{"code": "no_api_key", "message": "No API Key provided"}],
      "messages": [],
      "links": {"self": "/v2/skins?size=2"}
    }
    """;

    [Fact]
    public void ParseList_EmptyOrNullJson_ReturnsEmpty()
    {
        Assert.Empty(MineSkinBrowser.ParseList(string.Empty, 10, null));
        Assert.Empty(MineSkinBrowser.ParseList(null!, 10, null));
    }

    [Fact]
    public void ParseList_MalformedJson_ReturnsEmpty()
    {
        Assert.Empty(MineSkinBrowser.ParseList("{not json}", 10, null));
        Assert.Empty(MineSkinBrowser.ParseList("not json at all", 10, null));
    }

    [Fact]
    public void ParseList_JsonWithoutSkinsArray_ReturnsEmpty()
    {
        Assert.Empty(MineSkinBrowser.ParseList("""{"success":true}""", 10, null));
        Assert.Empty(MineSkinBrowser.ParseList("""{"skins":42}""", 10, null));
    }

    [Fact]
    public void ParseList_RealShape_YieldsTwoEntries()
    {
        var result = MineSkinBrowser.ParseList(RealShapeJson, 10, null);

        Assert.Equal(2, result.Count);

        var first = result[0];
        Assert.Equal("dd4869016758438b8cb408ab39730caa", first.Id);
        Assert.Equal(
            "https://textures.minecraft.net/texture/28f0c73d471fd25c8c527433cd5be82b0a8b0e9b2ae02d0842df72d3597caa54",
            first.PngDownloadUrl);
        Assert.Equal(first.PngDownloadUrl, first.ThumbnailUrl);
        Assert.Equal("https://minesk.in/f3a19bec", first.SourceUrl);
        Assert.Equal(SkinVariant.Classic, first.Variant);
        Assert.Equal("f3a19bec", first.UploaderName); // fallback when name is null
        Assert.Empty(first.Tags);
        Assert.Equal(0, first.Likes);

        var second = result[1];
        Assert.Equal("7090e39fde664229b2097dc184401f6e", second.Id);
        Assert.Equal("archmc-fd97569", second.UploaderName); // real name preferred over shortId
    }

    [Fact]
    public void ParseList_DropsEntriesMissingTexture()
    {
        const string Json = """
        {"skins":[
          {"uuid":"aaa","shortId":"a","name":"valid",
           "texture":"d4e7b865eff65aa7e1b476540cf1bde52eff6776d7ba7806e3cf96990fdd48f2"},
          {"uuid":"bbb","shortId":"b","name":"missing-texture"},
          {"uuid":"ccc","shortId":"c","name":"null-texture","texture":null}
        ]}
        """;

        var result = MineSkinBrowser.ParseList(Json, 10, null);

        Assert.Single(result);
        Assert.Equal("aaa", result[0].Id);
    }

    [Fact]
    public void ParseList_DedupesEntriesWithSameId()
    {
        const string Json = """
        {"skins":[
          {"uuid":"shared","shortId":"x","name":null,
           "texture":"d4e7b865eff65aa7e1b476540cf1bde52eff6776d7ba7806e3cf96990fdd48f2"},
          {"uuid":"shared","shortId":"y","name":"duplicate",
           "texture":"d4e7b865eff65aa7e1b476540cf1bde52eff6776d7ba7806e3cf96990fdd48f2"}
        ]}
        """;

        var result = MineSkinBrowser.ParseList(Json, 10, null);

        Assert.Single(result);
        Assert.Equal("shared", result[0].Id);
    }

    [Fact]
    public void ParseList_RespectsLimit()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("""{"skins":[""");
        for (var i = 0; i < 8; i++)
        {
            if (i > 0) sb.Append(',');
            var uuid = new string((char)('a' + i), 32);
            var tex = new string((char)('A' + i), 64);
            sb.Append($$"""{"uuid":"{{uuid}}","shortId":"s{{i}}","name":null,"texture":"{{tex}}"}""");
        }
        sb.Append("]}");

        var result = MineSkinBrowser.ParseList(sb.ToString(), 3, null);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseList_LimitZero_OrNegative_MeansNoCap()
    {
        var json = """
        {"skins":[
          {"uuid":"a","shortId":"a","name":null,"texture":"1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab"},
          {"uuid":"b","shortId":"b","name":null,"texture":"2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},
          {"uuid":"c","shortId":"c","name":null,"texture":"3cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}
        ]}
        """;

        Assert.Equal(3, MineSkinBrowser.ParseList(json, 0, null).Count);
        Assert.Equal(3, MineSkinBrowser.ParseList(json, -5, null).Count);
    }

    [Fact]
    public void ParseList_ClientSideFilter_MatchesNameOrShortId_CaseInsensitive()
    {
        const string Json = """
        {"skins":[
          {"uuid":"a","shortId":"alphaXYZ","name":"Cool-Skin",
           "texture":"1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab"},
          {"uuid":"b","shortId":"bravoXYZ","name":null,
           "texture":"2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},
          {"uuid":"c","shortId":"charlie","name":"Boring",
           "texture":"3cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}
        ]}
        """;

        // Substring "xyz" appears in shortId of (a) and (b).
        var byShortId = MineSkinBrowser.ParseList(Json, 10, "xyz");
        Assert.Equal(2, byShortId.Count);
        Assert.Contains(byShortId, s => s.Id == "a");
        Assert.Contains(byShortId, s => s.Id == "b");

        // Substring "cool" appears only in the name of (a).
        var byName = MineSkinBrowser.ParseList(Json, 10, "cool");
        Assert.Single(byName);
        Assert.Equal("a", byName[0].Id);

        // Empty query returns everything (parser treats empty as "no filter").
        var noFilter = MineSkinBrowser.ParseList(Json, 10, string.Empty);
        Assert.Equal(3, noFilter.Count);
    }

    [Fact]
    public void ParseList_AllSkinsReportClassicVariant()
    {
        // MineSkin's anonymous feed does not expose model. Every record must surface
        // as Classic so the existing upload pipeline default applies.
        var result = MineSkinBrowser.ParseList(RealShapeJson, 10, null);
        Assert.All(result, s => Assert.Equal(SkinVariant.Classic, s.Variant));
    }

    [Fact]
    public void ParseList_PngUrlPointsAtTextureCdn()
    {
        var result = MineSkinBrowser.ParseList(RealShapeJson, 10, null);
        Assert.All(result, s => Assert.StartsWith(MineSkinBrowser.TextureCdn, s.PngDownloadUrl));
    }

    [Fact]
    public void ParseList_SourceUrl_UsesShortId_WhenPresent()
    {
        const string Json = """
        {"skins":[
          {"uuid":"abc","shortId":"sh1","name":null,
           "texture":"1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab"}
        ]}
        """;
        var result = MineSkinBrowser.ParseList(Json, 10, null);
        Assert.Single(result);
        Assert.Equal("https://minesk.in/sh1", result[0].SourceUrl);
    }

    [Fact]
    public void ParseList_SourceUrl_FallsBackToUuid_WhenShortIdMissing()
    {
        const string Json = """
        {"skins":[
          {"uuid":"abc","name":null,
           "texture":"1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab"}
        ]}
        """;
        var result = MineSkinBrowser.ParseList(Json, 10, null);
        Assert.Single(result);
        Assert.Equal("https://minesk.in/abc", result[0].SourceUrl);
    }
}
