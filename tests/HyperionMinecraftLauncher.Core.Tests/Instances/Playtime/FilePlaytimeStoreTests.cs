using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Playtime;

public class FilePlaytimeStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hyperion-playtime-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "playtime.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static PlaySession Session(string id, int pid, DateTimeOffset at, string? gameDir = null)
        => new() { InstanceId = id, ProcessId = pid, StartedAt = at, GameDirectory = gameDir };

    [Fact]
    public async Task GetTotal_UnknownInstance_IsZero()
    {
        var store = new FilePlaytimeStore(FilePath);

        Assert.Equal(TimeSpan.Zero, await store.GetTotalAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task BeginThenEnd_CreditsSessionLength_AndClearsJournal()
    {
        var store = new FilePlaytimeStore(FilePath);
        await store.BeginSessionAsync(Session("a", 100, T0), CancellationToken.None);

        var credited = await store.EndSessionAsync(100, T0.AddMinutes(90), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(90), credited);
        Assert.Equal(TimeSpan.FromMinutes(90), await store.GetTotalAsync("a", CancellationToken.None));
        Assert.Empty(await store.ListActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sessions_Accumulate_PerInstance_AndSurviveReload()
    {
        var store = new FilePlaytimeStore(FilePath);
        await store.BeginSessionAsync(Session("a", 1, T0), CancellationToken.None);
        await store.EndSessionAsync(1, T0.AddHours(1), CancellationToken.None);
        await store.BeginSessionAsync(Session("a", 2, T0.AddHours(2)), CancellationToken.None);
        await store.EndSessionAsync(2, T0.AddHours(2.5), CancellationToken.None);
        await store.BeginSessionAsync(Session("b", 3, T0), CancellationToken.None);
        await store.EndSessionAsync(3, T0.AddMinutes(10), CancellationToken.None);

        var reloaded = new FilePlaytimeStore(FilePath);

        Assert.Equal(TimeSpan.FromHours(1.5), await reloaded.GetTotalAsync("a", CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(10), await reloaded.GetTotalAsync("b", CancellationToken.None));
    }

    [Fact]
    public async Task ActiveSession_IsJournaledOnDisk_ForRecoveryAfterRestart()
    {
        var store = new FilePlaytimeStore(FilePath);
        await store.BeginSessionAsync(Session("a", 42, T0, @"C:\games\a"), CancellationToken.None);

        var active = await new FilePlaytimeStore(FilePath).ListActiveAsync(CancellationToken.None);

        var s = Assert.Single(active);
        Assert.Equal(("a", 42, T0, @"C:\games\a"), (s.InstanceId, s.ProcessId, s.StartedAt, s.GameDirectory));
    }

    [Fact]
    public async Task EndSession_UnknownPid_CreditsNothing()
    {
        var store = new FilePlaytimeStore(FilePath);

        Assert.Equal(TimeSpan.Zero, await store.EndSessionAsync(999, T0, CancellationToken.None));
    }

    [Fact]
    public async Task EndSession_ClockWentBackwards_CreditsZeroNotNegative()
    {
        var store = new FilePlaytimeStore(FilePath);
        await store.BeginSessionAsync(Session("a", 1, T0), CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, await store.EndSessionAsync(1, T0.AddMinutes(-5), CancellationToken.None));
        Assert.Equal(TimeSpan.Zero, await store.GetTotalAsync("a", CancellationToken.None));
    }

    [Fact]
    public async Task EndSession_AbsurdlyLongSession_IsCapped()
    {
        var store = new FilePlaytimeStore(FilePath);
        await store.BeginSessionAsync(Session("a", 1, T0), CancellationToken.None);

        var credited = await store.EndSessionAsync(1, T0.AddDays(30), CancellationToken.None);

        Assert.Equal(FilePlaytimeStore.MaxSessionLength, credited);
    }

    [Fact]
    public async Task CorruptFile_IsTreatedAsEmpty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(FilePath, "{ not json");
        var store = new FilePlaytimeStore(FilePath);

        Assert.Equal(TimeSpan.Zero, await store.GetTotalAsync("a", CancellationToken.None));
        await store.BeginSessionAsync(Session("a", 1, T0), CancellationToken.None);
        Assert.Single(await store.ListActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentEnds_DoNotLoseCredit()
    {
        var store = new FilePlaytimeStore(FilePath);
        foreach (var pid in Enumerable.Range(1, 20))
            await store.BeginSessionAsync(Session("a", pid, T0), CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(1, 20).Select(pid =>
            store.EndSessionAsync(pid, T0.AddMinutes(1), CancellationToken.None)));

        Assert.Equal(TimeSpan.FromMinutes(20), await store.GetTotalAsync("a", CancellationToken.None));
    }

    [Fact]
    public void EstimateOrphanEnd_UsesLatestLogWrite_WhenItFallsInsideTheSession()
    {
        var s = Session("a", 1, T0);

        Assert.Equal(T0.AddHours(2), PlaySessionRecovery.EstimateEnd(s, T0.AddHours(2), now: T0.AddDays(1)));
    }

    [Theory]
    [InlineData(-1.0)]   // log older than the session: this session never wrote it
    [InlineData(48.0)]   // log newer than "now": clock skew, don't trust it
    public void EstimateOrphanEnd_LogOutsideSession_IsUnknown(double hoursFromStart)
    {
        var s = Session("a", 1, T0);

        Assert.Null(PlaySessionRecovery.EstimateEnd(s, T0.AddHours(hoursFromStart), now: T0.AddDays(1)));
    }

    [Fact]
    public void EstimateOrphanEnd_NoLog_IsUnknown()
    {
        Assert.Null(PlaySessionRecovery.EstimateEnd(Session("a", 1, T0), lastLogWriteUtc: null, now: T0.AddDays(1)));
    }
}
