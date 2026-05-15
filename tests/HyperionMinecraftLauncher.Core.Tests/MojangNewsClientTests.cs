using System.IO;
using System.Linq;
using System.Text;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class MojangNewsClientTests
{
    [Fact]
    public void Parse_ValidFeed_MapsEveryField()
    {
        const string json = """
        {
          "version": 1,
          "entries": [
            {
              "title": "Discover Planet Earth III DLC",
              "category": "Minecraft for Windows",
              "date": "2024-01-16",
              "text": "Step into a wondrous world...",
              "playPageImage": {
                "title": "img",
                "url": "/images/play_700x466.jpeg"
              },
              "newsPageImage": {
                "title": "img",
                "url": "/images/news_772x350.jpeg"
              },
              "readMoreLink": "https://www.minecraft.net/article/planet-earth-iii",
              "newsType": ["News page", "Bedrock"],
              "id": "2OKkEL0h71H7dyzTilA8ah"
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = MojangNewsClient.Parse(stream);

        var entry = Assert.Single(result);
        Assert.Equal("Discover Planet Earth III DLC", entry.Title);
        Assert.Equal("Minecraft for Windows", entry.Category);
        Assert.Equal("2024-01-16", entry.Date);
        Assert.Equal("Step into a wondrous world...", entry.Text);
        // Image preference: newsPageImage wins over playPageImage.
        Assert.Equal("https://launchercontent.mojang.com/images/news_772x350.jpeg", entry.ImageUrl);
        Assert.Equal("https://www.minecraft.net/article/planet-earth-iii", entry.ReadMoreLink);
        Assert.Equal("2OKkEL0h71H7dyzTilA8ah", entry.Id);
    }

    [Fact]
    public void Parse_FallsBackToPlayPageImage_WhenNewsPageImageMissing()
    {
        const string json = """
        { "entries": [ { "title": "X", "category": "Y", "date": "Z", "text": "T",
                         "playPageImage": { "url": "/images/p.jpg" } } ] }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = MojangNewsClient.Parse(stream);

        Assert.Equal("https://launchercontent.mojang.com/images/p.jpg", result[0].ImageUrl);
    }

    [Fact]
    public void Parse_NoEntries_ReturnsEmpty()
    {
        const string json = """{ "version": 1 }""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Empty(MojangNewsClient.Parse(stream));
    }

    [Fact]
    public void Parse_AbsoluteImageUrl_PassedThroughUnchanged()
    {
        const string json = """
        { "entries": [ { "title": "X", "category": "Y", "date": "Z", "text": "T",
                         "newsPageImage": { "url": "https://cdn.example.com/img.jpg" } } ] }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = MojangNewsClient.Parse(stream);

        Assert.Equal("https://cdn.example.com/img.jpg", result[0].ImageUrl);
    }
}
