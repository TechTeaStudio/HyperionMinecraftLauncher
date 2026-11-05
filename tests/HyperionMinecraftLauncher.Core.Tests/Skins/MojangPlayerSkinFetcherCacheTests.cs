using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

/// <summary>
/// A skin uploaded from the launcher replaces the cached one, so the next start shows it rather
/// than the skin it replaced (the cache lives for hours).
/// </summary>
public class MojangPlayerSkinFetcherCacheTests : IDisposable
{
    private const string Uuid = "1ff59ff9e67547719d2efb2ab06093ca";
    private static readonly byte[] OldSkin = { 0x89, 0x50, 0x4E, 0x47, 1 };
    private static readonly byte[] NewSkin = { 0x89, 0x50, 0x4E, 0x47, 2 };
    private static readonly byte[] Cape = { 0x89, 0x50, 0x4E, 0x47, 3 };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hypmcl-skincache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task RememberedSkin_IsWhatTheNextFetchReturns_WithoutNetwork()
    {
        var handler = new CountingHandler();
        var fetcher = new MojangPlayerSkinFetcher(new HttpClient(handler), new FileCache(_root));

        await fetcher.RememberAsync(Uuid, NewSkin, slim: true, Cape, CancellationToken.None);
        var info = await fetcher.FetchAsync(Uuid, CancellationToken.None);

        Assert.Equal(NewSkin, info!.SkinPng);
        Assert.True(info.IsSlim);
        Assert.Equal(Cape, info.CapePng);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RememberedSkin_ReplacesTheSkinCachedBefore()
    {
        var fetcher = new MojangPlayerSkinFetcher(new HttpClient(new CountingHandler()), new FileCache(_root));
        await fetcher.RememberAsync(Uuid, OldSkin, slim: false, Cape, CancellationToken.None);

        await fetcher.RememberAsync(Uuid, NewSkin, slim: true, capePng: null, CancellationToken.None);
        var info = await fetcher.FetchAsync(Uuid, CancellationToken.None);

        Assert.Equal(NewSkin, info!.SkinPng);
        Assert.True(info.IsSlim);
        Assert.Null(info.CapePng);
    }

    [Fact]
    public async Task Remember_WithoutCacheOrWithBadInput_DoesNothing()
    {
        var noCache = new MojangPlayerSkinFetcher(new HttpClient(new CountingHandler()));
        await noCache.RememberAsync(Uuid, NewSkin, slim: false, null, CancellationToken.None);

        var fetcher = new MojangPlayerSkinFetcher(new HttpClient(new CountingHandler()), new FileCache(_root));
        await fetcher.RememberAsync("not-a-uuid", NewSkin, slim: false, null, CancellationToken.None);
        await fetcher.RememberAsync(Uuid, Array.Empty<byte>(), slim: false, null, CancellationToken.None);

        Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length > 0);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
