using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.News;

public class FallbackNewsClientTests
{
    private static readonly NewsEntry A = new() { Title = "a", Category = "", Date = "2026-09-15", Text = "" };
    private static readonly NewsEntry B = new() { Title = "b", Category = "", Date = "2026-09-14", Text = "" };

    [Fact]
    public async Task PrimaryHasNews_SecondaryIsNotAsked()
    {
        var secondary = new FakeClient(B);
        var client = new FallbackNewsClient(new FakeClient(A), secondary);

        var result = await client.FetchAsync(CancellationToken.None);

        Assert.Equal("a", Assert.Single(result).Title);
        Assert.Equal(0, secondary.Calls);
    }

    [Fact]
    public async Task PrimaryEmpty_FallsBackToSecondary()
    {
        var client = new FallbackNewsClient(new FakeClient(), new FakeClient(B));

        Assert.Equal("b", Assert.Single(await client.FetchAsync(CancellationToken.None)).Title);
    }

    [Fact]
    public async Task PrimaryThrows_FallsBackToSecondary()
    {
        var client = new FallbackNewsClient(new FakeClient { Throw = true }, new FakeClient(B));

        Assert.Equal("b", Assert.Single(await client.FetchAsync(CancellationToken.None)).Title);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var client = new FallbackNewsClient(new FakeClient { Cancel = true }, new FakeClient(B));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LauncherFeed_ReadsTheLiveV2Feed()
    {
        Uri? requested = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            requested = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"version":1,"entries":[{"title":"A fight for every soul","category":"Minecraft for Windows",
                      "date":"2026-09-16","text":"t","newsPageImage":{"url":"/v2/images/MCD2Image1772x350.png"},
                      "readMoreLink":"https://www.creeperbattle.com/","id":"x"}]}
                    """, Encoding.UTF8, "application/json"),
            };
        }));

        var entries = await new MojangNewsClient(http).FetchAsync(CancellationToken.None);

        Assert.Equal("https://launchercontent.mojang.com/v2/news.json", requested!.ToString());
        Assert.Equal("https://launchercontent.mojang.com/v2/images/MCD2Image1772x350.png", Assert.Single(entries).ImageUrl);
    }

    private sealed class FakeClient(params NewsEntry[] entries) : INewsClient
    {
        public bool Throw { get; init; }
        public bool Cancel { get; init; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Cancel) throw new OperationCanceledException();
            if (Throw) throw new InvalidDataException("schema drift");
            return Task.FromResult<IReadOnlyList<NewsEntry>>(entries);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
