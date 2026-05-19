using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Verify that <see cref="MineSkinBrowser"/> shapes its outgoing HTTP request URL
/// correctly for the new paginated calls added in v0.32.4. These tests use a stub
/// <see cref="HttpMessageHandler"/> so no real network traffic is generated; the
/// stub records every URL it sees so the test can assert on the query string.
/// </summary>
public class MineSkinBrowserPaginationTests
{
    /// <summary>
    /// Two-entry response shaped like a real MineSkin v2 reply. Reused across tests.
    /// </summary>
    private const string FixtureJson = /*lang=json,strict*/ """
    {
      "success": true,
      "skins": [
        {
          "uuid": "dd4869016758438b8cb408ab39730caa",
          "shortId": "f3a19bec",
          "name": null,
          "texture": "28f0c73d471fd25c8c527433cd5be82b0a8b0e9b2ae02d0842df72d3597caa54"
        },
        {
          "uuid": "7090e39fde664229b2097dc184401f6e",
          "shortId": "936cc382",
          "name": "archmc-fd97569",
          "texture": "6833d8fd032f250cd3daab0ff913626e1d4e973ae8e9bb10a73a91e397f2bc08"
        }
      ]
    }
    """;

    [Fact]
    public async Task ListTrendingAsync_Page0_Limit60_SendsPage0Size60()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        var result = await browser.ListTrendingAsync(page: 0, limit: 60, CancellationToken.None);

        Assert.Single(handler.Urls);
        var url = handler.Urls[0];
        Assert.Contains("page=0", url, StringComparison.Ordinal);
        Assert.Contains("size=60", url, StringComparison.Ordinal);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListTrendingAsync_Page2_Limit24_SendsPage2Size24()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        await browser.ListTrendingAsync(page: 2, limit: 24, CancellationToken.None);

        Assert.Single(handler.Urls);
        var url = handler.Urls[0];
        Assert.Contains("page=2", url, StringComparison.Ordinal);
        Assert.Contains("size=24", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTrendingAsync_NegativePage_ClampsToZero()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        await browser.ListTrendingAsync(page: -5, limit: 30, CancellationToken.None);

        Assert.Single(handler.Urls);
        Assert.Contains("page=0", handler.Urls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTrendingAsync_LimitOverCap_ClampsToMaxApiPageSize()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        await browser.ListTrendingAsync(page: 0, limit: 9999, CancellationToken.None);

        Assert.Single(handler.Urls);
        Assert.Contains($"size={MineSkinBrowser.MaxApiPageSize}", handler.Urls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTrendingAsync_LegacyOverload_DefaultsToPage0()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        // The single-arg overload from v0.32.3 must still hit page 0 so existing callers
        // keep working without code changes.
        await browser.ListTrendingAsync(limit: 30, CancellationToken.None);

        Assert.Single(handler.Urls);
        Assert.Contains("page=0", handler.Urls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_Page3_Limit50_SendsPageAndNameQuery()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        await browser.SearchAsync("steve", page: 3, limit: 50, CancellationToken.None);

        Assert.Single(handler.Urls);
        var url = handler.Urls[0];
        Assert.Contains("page=3", url, StringComparison.Ordinal);
        Assert.Contains("name=steve", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_FallsBackToTrendingOnSamePage()
    {
        var handler = new RecordingHandler(FixtureJson);
        using var http = new HttpClient(handler);
        var browser = new MineSkinBrowser(http);

        await browser.SearchAsync(string.Empty, page: 1, limit: 24, CancellationToken.None);

        Assert.Single(handler.Urls);
        var url = handler.Urls[0];
        Assert.Contains("page=1", url, StringComparison.Ordinal);
        Assert.DoesNotContain("name=", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// Captures every outgoing URL and returns a fixed JSON body so tests can assert
    /// on the request shape without standing up a real server. Uses
    /// <see cref="HttpStatusCode.OK"/> so <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>
    /// passes inside the browser.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public List<string> Urls { get; } = new();

        public RecordingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
        }
    }
}
