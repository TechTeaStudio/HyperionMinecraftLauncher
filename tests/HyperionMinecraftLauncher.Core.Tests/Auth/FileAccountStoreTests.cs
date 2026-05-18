using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Auth;

public class FileAccountStoreTests : IDisposable
{
    private readonly string _temp;
    private readonly string _v2Path;
    private readonly string _v1Path;

    public FileAccountStoreTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-accounts-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
        _v2Path = Path.Combine(_temp, "accounts.v2.json");
        _v1Path = Path.Combine(_temp, "accounts.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListAsync_NoFile_ReturnsEmpty()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
    }

    [Fact]
    public async Task SaveAsync_ThenListAsync_RoundTripsMultipleAccounts()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);

        var a = new Account
        {
            Id = "msalhome-abc",
            Username = "Alex",
            Uuid = "11111111111111111111111111111111",
            SkinHeadUri = "avares://HyperionMinecraftLauncher/Assets/Icons/MC/steve_face.png",
            LastUsedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero),
            IsOffline = false,
        };
        var b = new Account
        {
            Id = "msalhome-def",
            Username = "Steve",
            Uuid = "22222222222222222222222222222222",
            SkinHeadUri = null,
            LastUsedAt = new DateTimeOffset(2025, 2, 2, 8, 30, 0, TimeSpan.Zero),
            IsOffline = false,
        };

        await store.SaveAsync(a, CancellationToken.None);
        await store.SaveAsync(b, CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, x => x.Id == "msalhome-abc" && x.Username == "Alex");
        Assert.Contains(list, x => x.Id == "msalhome-def" && x.Username == "Steve");
        Assert.True(File.Exists(_v2Path));
    }

    [Fact]
    public async Task SaveAsync_SameId_OverwritesExisting()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var first = new Account
        {
            Id = "id-1",
            Username = "Old",
            Uuid = "uuuu",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(first, CancellationToken.None);

        var updated = first with { Username = "New" };
        await store.SaveAsync(updated, CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("New", list[0].Username);
    }

    [Fact]
    public async Task SetActiveAsync_ThenGetActive_ReturnsAccount()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var a = new Account
        {
            Id = "active-1",
            Username = "ActiveUser",
            Uuid = "uuid-1",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(a, CancellationToken.None);
        await store.SetActiveAsync("active-1", CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);
        Assert.NotNull(active);
        Assert.Equal("active-1", active!.Id);
    }

    [Fact]
    public async Task SetActiveAsync_Null_ClearsActive()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var a = new Account
        {
            Id = "id",
            Username = "U",
            Uuid = "u",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(a, CancellationToken.None);
        await store.SetActiveAsync("id", CancellationToken.None);
        await store.SetActiveAsync(null, CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);
        Assert.Null(active);
    }

    [Fact]
    public async Task GetActiveAsync_NoActiveSet_ReturnsNull()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var a = new Account
        {
            Id = "id",
            Username = "U",
            Uuid = "u",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(a, CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);
        Assert.Null(active);
    }

    [Fact]
    public async Task RemoveAsync_RemovesAccount_AndClearsActiveIfMatching()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var a = new Account
        {
            Id = "kill-me",
            Username = "Doomed",
            Uuid = "u",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(a, CancellationToken.None);
        await store.SetActiveAsync("kill-me", CancellationToken.None);

        await store.RemoveAsync("kill-me", CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
        var active = await store.GetActiveAsync(CancellationToken.None);
        Assert.Null(active);
    }

    [Fact]
    public async Task RemoveAsync_NonActiveAccount_KeepsActive()
    {
        var store = new FileAccountStore(_v2Path, _v1Path);
        var a = new Account
        {
            Id = "keep-me",
            Username = "Active",
            Uuid = "u1",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        var b = new Account
        {
            Id = "kill-me",
            Username = "Other",
            Uuid = "u2",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        };
        await store.SaveAsync(a, CancellationToken.None);
        await store.SaveAsync(b, CancellationToken.None);
        await store.SetActiveAsync("keep-me", CancellationToken.None);

        await store.RemoveAsync("kill-me", CancellationToken.None);

        var active = await store.GetActiveAsync(CancellationToken.None);
        Assert.NotNull(active);
        Assert.Equal("keep-me", active!.Id);
    }

    [Fact]
    public async Task ListAsync_MigratesFromV1LegacyFile_ReturnsSingleAccount()
    {
        // Legacy "accounts.json" is the XboxAuthNet account-manager cache. We don't try to
        // parse its real format - the migration path produces a one-entry list with a synthetic
        // id so the user sees a placeholder account they can use to trigger a fresh sign-in.
        // The contract: when v2 is absent but v1 exists, ListAsync returns 1 account.
        File.WriteAllText(_v1Path, "{\"accounts\":[{\"foo\":\"bar\"}]}");
        Assert.False(File.Exists(_v2Path));

        var store = new FileAccountStore(_v2Path, _v1Path);
        var list = await store.ListAsync(CancellationToken.None);

        Assert.Single(list);
        // V1 must be left in place for back-compat with the old code path.
        Assert.True(File.Exists(_v1Path));
    }

    [Fact]
    public async Task ListAsync_CorruptV2File_ReturnsEmpty()
    {
        File.WriteAllText(_v2Path, "{ not json");
        var store = new FileAccountStore(_v2Path, _v1Path);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Empty(list);
    }

    [Fact]
    public async Task SaveAsync_CreatesParentDirectory()
    {
        var deep = Path.Combine(_temp, "a", "b", "accounts.v2.json");
        var deepV1 = Path.Combine(_temp, "a", "b", "accounts.json");
        var store = new FileAccountStore(deep, deepV1);

        await store.SaveAsync(new Account
        {
            Id = "z",
            Username = "z",
            Uuid = "z",
            LastUsedAt = DateTimeOffset.UtcNow,
            IsOffline = false,
        }, CancellationToken.None);

        Assert.True(File.Exists(deep));
    }
}
