using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.News;

public class MinecraftNetNewsClientTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "hyperion-news-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
    }

    // Trimmed copy of a real response from the minecraft.net article-grid search API.
    private const string Page = """
        {"result":{"results":[
          {"title":"Play Wilderness Bound","neutralTitle":"",
           "url":"https://www.minecraft.net/en-us/article/play-wilderness-bound-game-drop",
           "superhead":"","description":"Wilderness Bound is out now in Minecraft! ",
           "author":"Linn Viberg",
           "image":"https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/key-art/Minecraft_WildernessBound.Net_1170x500.jpg",
           "tags":["minecraft:news/updates","minecraft:games/minecraft-java-&-bedrock/game-drops-&-updates/drop-3-2026","article-page"],
           "time":1789491600,"type":"News","locale":"en-us"},
          {"title":"Minecraft Java Edition 26.3","url":"https://www.minecraft.net/en-us/article/minecraft-java-edition-26-3",
           "description":"Minecraft 26.3","image":"/content/dam/minecraftnet/java/26-3.jpg",
           "tags":["minecraft:news","minecraft:games/minecraft-java","article-page"],"time":1789400000,"type":"News"},
          {"title":"Realms Plus spotlight","url":"https://www.minecraft.net/en-us/article/realms-plus",
           "description":"x","image":"https://www.minecraft.net/a.jpg",
           "tags":["minecraft:marketplace/realms/realms-plus","article-page"],"time":1789300000,"type":"News"},
          {"title":"Deep dive","url":"https://www.minecraft.net/en-us/article/deep","description":"y","image":"",
           "tags":["minecraft:news/deep-dives"],"time":1789200000,"type":"News"}
        ],"numFound":3005,"page":1}}
        """;

    [Fact]
    public void Parse_MapsTheArticleGridFields()
    {
        var entries = MinecraftNetNewsClient.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Page)));

        Assert.Equal(4, entries.Count);
        var first = entries[0];
        Assert.Equal("Play Wilderness Bound", first.Title);
        Assert.Equal("Wilderness Bound is out now in Minecraft!", first.Text);
        Assert.Equal("https://www.minecraft.net/en-us/article/play-wilderness-bound-game-drop", first.ReadMoreLink);
        Assert.Equal(first.ReadMoreLink, first.Id);
        Assert.Equal("https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/key-art/Minecraft_WildernessBound.Net_1170x500.jpg", first.ImageUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789491600).UtcDateTime.ToString("yyyy-MM-dd"), first.Date);
    }

    [Fact]
    public void Parse_DerivesCategoryFromTheGameTag()
    {
        var entries = MinecraftNetNewsClient.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Page)));

        Assert.Equal(new[] { "Minecraft", "Minecraft: Java Edition", "Realms", "Minecraft" },
            entries.Select(e => e.Category).ToArray());
    }

    [Fact]
    public void Parse_RelativeImage_IsResolvedAgainstMinecraftNet_AndBlankImageIsNull()
    {
        var entries = MinecraftNetNewsClient.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Page)));

        Assert.Equal("https://www.minecraft.net/content/dam/minecraftnet/java/26-3.jpg", entries[1].ImageUrl);
        Assert.Null(entries[3].ImageUrl);
    }

    [Fact]
    public async Task FetchAsync_QueriesRecentNews_AndCachesTheResponse()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new StubHandler(req =>
        {
            requests.Add(req.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page, Encoding.UTF8, "application/json") };
        }));
        var cache = new FileCache(_cacheDir);

        var first = await new MinecraftNetNewsClient(http, cache).FetchAsync(CancellationToken.None);
        var second = await new MinecraftNetNewsClient(http, cache).FetchAsync(CancellationToken.None);

        Assert.Equal(4, first.Count);
        Assert.Equal(4, second.Count);
        var uri = Assert.Single(requests); // second call is served from the cache
        Assert.Equal("net-secondary.web.minecraft-services.net", uri.Host);
        Assert.Contains("category=News", uri.Query);
        Assert.Contains("sortType=Recent", uri.Query);
        Assert.Contains("pageSize=24", uri.Query); // the API answers 400 above 24
    }

    [Fact]
    public async Task FetchAsync_NetworkDown_NoCache_ReturnsEmpty()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));

        Assert.Empty(await new MinecraftNetNewsClient(http).FetchAsync(CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
