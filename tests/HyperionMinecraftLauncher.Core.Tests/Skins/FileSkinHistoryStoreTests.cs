using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

public class FileSkinHistoryStoreTests : IDisposable
{
    private readonly string _root;

    public FileSkinHistoryStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hypmcl-skinhistory-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task Append_Then_List_RoundTripsEntry()
    {
        var store = new FileSkinHistoryStore(_root);
        var png = MakePng();

        await store.AppendAsync(png, SkinVariant.Slim, CancellationToken.None);
        var list = await store.ListAsync(CancellationToken.None);

        var entry = Assert.Single(list);
        Assert.Equal(SkinVariant.Slim, entry.Variant);
        Assert.True(File.Exists(entry.FilePath), $"Expected PNG on disk at {entry.FilePath}");
        Assert.Equal(png.Length, new FileInfo(entry.FilePath).Length);
    }

    [Fact]
    public async Task Append_Eleven_Entries_DropsOldestAndKeepsTen()
    {
        var store = new FileSkinHistoryStore(_root);

        for (int i = 0; i < 11; i++)
            await store.AppendAsync(MakePng(seed: (byte)i), SkinVariant.Classic, CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(FileSkinHistoryStore.Capacity, list.Count);
        Assert.Equal(10, list.Count); // explicit check matching acceptance criteria

        // On-disk PNG file count tracks the index exactly.
        var pngs = Directory.GetFiles(_root, "*.png", SearchOption.TopDirectoryOnly);
        Assert.Equal(10, pngs.Length);
    }

    [Fact]
    public async Task Append_OrdersNewestFirst()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var store = new FileSkinHistoryStore(_root, () => clock.Now);

        await store.AppendAsync(MakePng(seed: 1), SkinVariant.Classic, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await store.AppendAsync(MakePng(seed: 2), SkinVariant.Slim, CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Equal(SkinVariant.Slim, list[0].Variant);
        Assert.Equal(SkinVariant.Classic, list[1].Variant);
        Assert.True(list[0].SavedAt > list[1].SavedAt);
    }

    [Fact]
    public async Task Clear_RemovesEveryFileAndIndex()
    {
        var store = new FileSkinHistoryStore(_root);
        await store.AppendAsync(MakePng(), SkinVariant.Classic, CancellationToken.None);
        await store.AppendAsync(MakePng(), SkinVariant.Slim, CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
        Assert.Empty(Directory.GetFiles(_root, "*.png"));
        Assert.False(File.Exists(Path.Combine(_root, "index.json")));
    }

    [Fact]
    public async Task Append_PersistsAcrossNewStoreInstance()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var first = new FileSkinHistoryStore(_root, () => clock.Now);
        await first.AppendAsync(MakePng(), SkinVariant.Slim, CancellationToken.None);

        var second = new FileSkinHistoryStore(_root, () => clock.Now);
        var list = await second.ListAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(SkinVariant.Slim, list[0].Variant);
    }

    [Fact]
    public async Task Append_EmptyBytes_Throws()
    {
        var store = new FileSkinHistoryStore(_root);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.AppendAsync(Array.Empty<byte>(), SkinVariant.Classic, CancellationToken.None));
    }

    private static byte[] MakePng(byte seed = 0xCC)
    {
        // 8-byte PNG header + 8 bytes of arbitrary payload.
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, seed, seed, seed, seed, seed, seed, seed, seed };
        return data;
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; private set; }
        public TestClock(DateTimeOffset start) => Now = start;
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }
}
