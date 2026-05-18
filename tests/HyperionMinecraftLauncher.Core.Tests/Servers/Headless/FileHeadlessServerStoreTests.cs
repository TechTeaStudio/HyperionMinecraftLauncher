using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers.Headless;

/// <summary>
/// Covers CRUD on the file-backed headless server store. Each test instance gets its own
/// temp root so the assertions run against the directory layout in isolation.
/// </summary>
public class FileHeadlessServerStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileHeadlessServerStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "HMLTests_HeadlessStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task CreateAsync_WritesMetadataAndSkeletonFiles()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var request = new HeadlessServerCreateRequest
        {
            Name = "Test Server",
            VersionId = "1.21.5",
            RamMb = 3072,
            Port = 25577,
        };

        var server = await store.CreateAsync(request, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(server.Id));
        Assert.Equal("Test Server", server.Name);
        Assert.Equal("1.21.5", server.VersionId);
        Assert.Equal(3072, server.RamMb);
        Assert.Equal(25577, server.Port);

        var dir = Path.Combine(_tempDir, server.Id);
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(dir, "metadata.json")));
        Assert.True(File.Exists(Path.Combine(dir, "eula.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "server.properties")));

        var eulaContents = await File.ReadAllTextAsync(Path.Combine(dir, "eula.txt"));
        Assert.Contains("eula=true", eulaContents);

        var propsContents = await File.ReadAllTextAsync(Path.Combine(dir, "server.properties"));
        Assert.Contains("server-port=25577", propsContents);
    }

    [Fact]
    public async Task CreateAsync_DefaultRamAndPort_AreApplied()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var request = new HeadlessServerCreateRequest
        {
            Name = "Defaults",
            VersionId = "1.21.5",
        };

        var server = await store.CreateAsync(request, CancellationToken.None);

        Assert.Equal(2048, server.RamMb);
        Assert.Equal(25565, server.Port);
    }

    [Fact]
    public async Task ListAsync_NoServers_ReturnsEmpty()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListAsync_MultipleServers_AreReturnedNewestFirst()
    {
        var store = new FileHeadlessServerStore(_tempDir);

        var first = await store.CreateAsync(
            new HeadlessServerCreateRequest { Name = "Older", VersionId = "1.20.4" }, CancellationToken.None);
        // Force a measurable gap so newest-first ordering is deterministic on fast filesystems.
        await Task.Delay(20);
        var second = await store.CreateAsync(
            new HeadlessServerCreateRequest { Name = "Newer", VersionId = "1.21.5" }, CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
    }

    [Fact]
    public async Task GetAsync_KnownId_RoundTripsRecord()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var created = await store.CreateAsync(
            new HeadlessServerCreateRequest { Name = "Loaded", VersionId = "1.21.5", RamMb = 4096, Port = 25566 },
            CancellationToken.None);

        var fresh = new FileHeadlessServerStore(_tempDir);
        var loaded = await fresh.GetAsync(created.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Equal("Loaded", loaded.Name);
        Assert.Equal("1.21.5", loaded.VersionId);
        Assert.Equal(4096, loaded.RamMb);
        Assert.Equal(25566, loaded.Port);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var loaded = await store.GetAsync("nonexistent-id", CancellationToken.None);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_RemovesServerFolder()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var created = await store.CreateAsync(
            new HeadlessServerCreateRequest { Name = "Doomed", VersionId = "1.21.5" }, CancellationToken.None);

        await store.DeleteAsync(created.Id, CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(_tempDir, created.Id)));
        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_NoThrow()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        await store.DeleteAsync("nonexistent-id", CancellationToken.None);
        Assert.True(Directory.Exists(_tempDir));
    }

    [Fact]
    public async Task ListAsync_SkipsFoldersWithoutMetadata()
    {
        var store = new FileHeadlessServerStore(_tempDir);
        var ok = await store.CreateAsync(
            new HeadlessServerCreateRequest { Name = "Good", VersionId = "1.21.5" }, CancellationToken.None);

        // Drop a stray subfolder that doesn't conform to the layout - should be ignored.
        Directory.CreateDirectory(Path.Combine(_tempDir, "stray"));

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(ok.Id, list[0].Id);
    }
}
