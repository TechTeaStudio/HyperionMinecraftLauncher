using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods;

public class ModrinthRepositoryTests
{
    private const string SearchJson = """
    {
      "hits": [
        {
          "project_id": "P7dR8mSH",
          "slug": "fabric-api",
          "title": "Fabric API",
          "description": "Lightweight and modular API.",
          "author": "modmuss50",
          "icon_url": "https://cdn.modrinth.com/data/P7dR8mSH/icon.png",
          "categories": ["library","fabric"],
          "downloads": 250000000
        },
        {
          "project_id": "AANobbMI",
          "slug": "sodium",
          "title": "Sodium",
          "description": "Modern rendering engine for Minecraft.",
          "author": "jellysquid3",
          "icon_url": null,
          "categories": ["optimization"],
          "downloads": 60000000
        }
      ],
      "offset": 0,
      "limit": 20,
      "total_hits": 2
    }
    """;

    private const string FilesJson = """
    [
      {
        "id": "abcd1234",
        "project_id": "P7dR8mSH",
        "name": "0.92.2+1.20.1",
        "version_number": "0.92.2+1.20.1",
        "game_versions": ["1.20.1"],
        "loaders": ["fabric"],
        "files": [
          {
            "url": "https://cdn.modrinth.com/data/P7dR8mSH/versions/abcd1234/fabric-api-0.92.2.jar",
            "filename": "fabric-api-0.92.2.jar",
            "primary": true,
            "size": 1234567,
            "hashes": { "sha1": "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef" }
          }
        ]
      }
    ]
    """;

    [Fact]
    public async Task SearchAsync_ParsesHitsIntoModRecords()
    {
        var handler = new CapturingHandler((req, _) =>
        {
            Assert.Contains("/v2/search", req.RequestUri!.ToString());
            return Reply(SearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        var repo = new ModrinthRepository(http);

        var hits = await repo.SearchAsync(new ModSearchQuery { Query = "fabric", Limit = 20 }, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal("P7dR8mSH", hits[0].Id);
        Assert.Equal("fabric-api", hits[0].Slug);
        Assert.Equal("Fabric API", hits[0].Name);
        Assert.Equal("modmuss50", hits[0].AuthorDisplay);
        Assert.Equal("https://cdn.modrinth.com/data/P7dR8mSH/icon.png", hits[0].IconUri);
        Assert.Equal(ModSource.Modrinth, hits[0].Source);
        Assert.Equal(250000000, hits[0].Downloads);
        Assert.Contains("library", hits[0].Categories);
        Assert.Null(hits[1].IconUri);
    }

    [Fact]
    public async Task SearchAsync_BuildsFacetsFromGameVersionAndLoader()
    {
        string? capturedUrl = null;
        var handler = new CapturingHandler((req, _) =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return Reply(SearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        var repo = new ModrinthRepository(http);

        await repo.SearchAsync(new ModSearchQuery
        {
            Query = "sodium",
            GameVersion = "1.20.1",
            Loader = ModLoader.Fabric,
        }, CancellationToken.None);

        Assert.NotNull(capturedUrl);
        Assert.Contains("query=sodium", capturedUrl);
        // facets must include both filters.
        Assert.Contains("facets=", capturedUrl);
        Assert.Contains("versions%3A1.20.1", capturedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("categories%3Afabric", capturedUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFilesAsync_ParsesPrimaryFileForGivenVersionAndLoader()
    {
        var handler = new CapturingHandler((req, _) =>
        {
            Assert.Contains("/v2/project/", req.RequestUri!.ToString());
            Assert.Contains("/version", req.RequestUri!.ToString());
            return Reply(FilesJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        var repo = new ModrinthRepository(http);

        var files = await repo.ListFilesAsync("P7dR8mSH", "1.20.1", ModLoader.Fabric, CancellationToken.None);

        var f = Assert.Single(files);
        Assert.Equal("abcd1234", f.FileId);
        Assert.Equal("P7dR8mSH", f.ModId);
        Assert.Equal("fabric-api-0.92.2.jar", f.Filename);
        Assert.Equal("https://cdn.modrinth.com/data/P7dR8mSH/versions/abcd1234/fabric-api-0.92.2.jar", f.DownloadUrl);
        Assert.Equal(1234567, f.FileSize);
        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", f.Sha1);
        Assert.Contains("1.20.1", f.GameVersions);
        Assert.Contains(ModLoader.Fabric, f.Loaders);
    }

    [Fact]
    public async Task DownloadAsync_StreamsBytesIntoDestination()
    {
        var payload = Encoding.UTF8.GetBytes("mod-bytes-stub");
        var handler = new CapturingHandler((req, _) =>
        {
            Assert.Equal("https://cdn.modrinth.com/data/X/file.jar", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        var repo = new ModrinthRepository(http);
        var modFile = new ModFile
        {
            ModId = "X", FileId = "1",
            DisplayName = "1.0",
            Filename = "file.jar",
            DownloadUrl = "https://cdn.modrinth.com/data/X/file.jar",
            FileSize = payload.Length,
        };

        using var ms = new MemoryStream();
        await repo.DownloadAsync(modFile, ms, null, CancellationToken.None);

        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task SearchAsync_EmptyHits_ReturnsEmptyList()
    {
        var handler = new CapturingHandler((_, _) => Reply("""{ "hits": [], "offset": 0, "limit": 20, "total_hits": 0 }"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        var repo = new ModrinthRepository(http);

        var hits = await repo.SearchAsync(new ModSearchQuery { Query = "doesntexist" }, CancellationToken.None);

        Assert.Empty(hits);
    }

    private static HttpResponseMessage Reply(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    internal sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
