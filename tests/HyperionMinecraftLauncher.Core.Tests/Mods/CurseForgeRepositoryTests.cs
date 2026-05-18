using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods;

public class CurseForgeRepositoryTests
{
    private const string SearchJson = """
    {
      "data": [
        {
          "id": 238222,
          "name": "JEI",
          "slug": "jei",
          "summary": "View items and recipes.",
          "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/jei" },
          "logo": { "url": "https://media.forgecdn.net/avatars/jei.png" },
          "authors": [{"name": "mezz"}],
          "categories": [{"name":"API and Library"}],
          "downloadCount": 100000000
        }
      ],
      "pagination": { "index": 0, "pageSize": 20, "resultCount": 1, "totalCount": 1 }
    }
    """;

    private const string FilesJson = """
    {
      "data": [
        {
          "id": 5555,
          "modId": 238222,
          "displayName": "jei-1.20.1-15.2.0.27.jar",
          "fileName": "jei-1.20.1-15.2.0.27.jar",
          "downloadUrl": "https://edge.forgecdn.net/files/5555/jei.jar",
          "fileLength": 222222,
          "hashes": [{ "value": "abc123", "algo": 1 }],
          "gameVersions": ["1.20.1", "Forge"]
        }
      ]
    }
    """;

    [Fact]
    public async Task SearchAsync_WithEmptyKey_ReturnsEmptyListAndLogsOnce()
    {
        var logger = new RecordingLogger();
        var repo = new CurseForgeRepository(new HttpClient(new ThrowingHandler()), apiKey: string.Empty, logger);

        var first = await repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None);
        var second = await repo.SearchAsync(new ModSearchQuery { Query = "y" }, CancellationToken.None);

        Assert.Empty(first);
        Assert.Empty(second);
        // Logs the warning exactly once across the two calls (per spec).
        var warnings = 0;
        foreach (var line in logger.WarnEntries)
            if (line.Contains("CurseForge API key not configured"))
                warnings++;
        Assert.Equal(1, warnings);
    }

    [Fact]
    public async Task ListFilesAsync_WithEmptyKey_ThrowsNotSupported()
    {
        var repo = new CurseForgeRepository(new HttpClient(new ThrowingHandler()), apiKey: string.Empty, new RecordingLogger());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            repo.ListFilesAsync("X", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_WithEmptyKey_ThrowsNotSupported()
    {
        var repo = new CurseForgeRepository(new HttpClient(new ThrowingHandler()), apiKey: string.Empty, new RecordingLogger());
        var file = new ModFile
        {
            ModId = "X", FileId = "1", DisplayName = "1",
            DownloadUrl = "https://example.invalid/file.jar",
        };

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            repo.DownloadAsync(file, ms, null, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_WithKey_AttachesApiKeyHeaderAndParses()
    {
        string? capturedKey = null;
        string? capturedUrl = null;
        var handler = new CapturingHandler((req, _) =>
        {
            capturedUrl = req.RequestUri!.ToString();
            if (req.Headers.TryGetValues("x-api-key", out var vals))
                capturedKey = string.Join(",", vals);
            return Reply(SearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, apiKey: "fake-key", new RecordingLogger());

        var hits = await repo.SearchAsync(new ModSearchQuery { Query = "jei", GameVersion = "1.20.1" }, CancellationToken.None);

        Assert.Equal("fake-key", capturedKey);
        Assert.Contains("gameId=432", capturedUrl);
        Assert.Contains("searchFilter=jei", capturedUrl);
        Assert.Single(hits);
        Assert.Equal("238222", hits[0].Id);
        Assert.Equal("jei", hits[0].Slug);
        Assert.Equal("JEI", hits[0].Name);
        Assert.Equal("mezz", hits[0].AuthorDisplay);
        Assert.Equal(ModSource.CurseForge, hits[0].Source);
        Assert.Equal(100000000, hits[0].Downloads);
    }

    [Fact]
    public async Task ListFilesAsync_WithKey_ParsesFiles()
    {
        var handler = new CapturingHandler((req, _) =>
        {
            Assert.Contains("/v1/mods/238222/files", req.RequestUri!.ToString());
            return Reply(FilesJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, apiKey: "fake", new RecordingLogger());

        var files = await repo.ListFilesAsync("238222", "1.20.1", ModLoader.Forge, CancellationToken.None);

        var f = Assert.Single(files);
        Assert.Equal("5555", f.FileId);
        Assert.Equal("238222", f.ModId);
        Assert.Equal("jei-1.20.1-15.2.0.27.jar", f.Filename);
        Assert.Equal("https://edge.forgecdn.net/files/5555/jei.jar", f.DownloadUrl);
        Assert.Equal(222222, f.FileSize);
        Assert.Contains("1.20.1", f.GameVersions);
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

    internal sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("HTTP must not be called for empty-key mode");
    }
}
