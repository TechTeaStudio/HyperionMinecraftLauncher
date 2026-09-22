using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Playtime;

public class MainViewModelInstanceCardTests
{
    private static readonly Instance FabricPack = new()
    {
        Id = "nestle",
        Name = "Nestle Optimized",
        VersionId = "26.1.2",
        Loader = ModLoader.Fabric,
        LoaderVersion = "0.19.3",
        LastPlayedAt = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.Zero),
    };

    private static readonly Instance VanillaPack = new() { Id = "plain", Name = "Plain", VersionId = "1.21.5" };

    private static MainViewModel NewVm(FakePlaytimeStore playtime, FakeModManager mods, StubLauncherService? service = null)
        => new(service ?? new StubLauncherService(), new RecordingLogger(),
            instanceModManager: mods, playtimeStore: playtime);

    [Fact]
    public async Task SelectingModdedInstance_FillsLoaderVersionModsAndPlaytime()
    {
        var playtime = new FakePlaytimeStore();
        playtime.Totals["nestle"] = TimeSpan.FromMinutes(90);
        var mods = new FakeModManager { Mods = { Mod("a.jar", true), Mod("b.jar", true), Mod("c.jar.disabled", false) } };
        var vm = NewVm(playtime, mods);

        vm.SelectedInstance = FabricPack;
        await vm.RefreshSelectedInstanceSummaryAsync();

        Assert.Equal("Fabric 0.19.3", vm.SelectedInstanceLoaderText);
        Assert.Equal(InstanceIcons.Planks, vm.SelectedInstanceLoaderIconKey);
        Assert.Equal("26.1.2", vm.SelectedInstanceVersionText);
        Assert.True(vm.IsSelectedInstanceModded);
        Assert.Equal("2", vm.SelectedInstanceModsText); // disabled jars don't count
        Assert.Equal(
            PlaytimeFormat.Format(TimeSpan.FromMinutes(90), Strings.InstanceCard_PlaytimeHoursMinutes, Strings.InstanceCard_PlaytimeMinutes),
            vm.SelectedInstancePlaytimeText);
        Assert.Equal(
            FabricPack.LastPlayedAt!.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
            vm.SelectedInstanceLastPlayedText);
    }

    [Fact]
    public async Task SelectingVanillaInstance_ShowsVanillaChip_AndHidesModsRow()
    {
        var vm = NewVm(new FakePlaytimeStore(), new FakeModManager());

        vm.SelectedInstance = VanillaPack;
        await vm.RefreshSelectedInstanceSummaryAsync();

        Assert.Equal(Strings.InstanceCard_Vanilla, vm.SelectedInstanceLoaderText);
        Assert.Equal(InstanceIcons.GrassBlock, vm.SelectedInstanceLoaderIconKey);
        Assert.False(vm.IsSelectedInstanceModded);
        Assert.Equal(Strings.InstanceCard_Never, vm.SelectedInstanceLastPlayedText);
        Assert.Equal(
            PlaytimeFormat.Format(TimeSpan.Zero, Strings.InstanceCard_PlaytimeHoursMinutes, Strings.InstanceCard_PlaytimeMinutes),
            vm.SelectedInstancePlaytimeText);
    }

    [Fact]
    public async Task LaunchingInstance_JournalsPlaySession_ForTheGameProcess()
    {
        var playtime = new FakePlaytimeStore();
        var service = new StubLauncherService
        {
            LaunchResultToReturn = new() { ProcessId = 4242, VersionName = "26.1.2" },
        };
        var vm = NewVm(playtime, new FakeModManager(), service);
        vm.Username = "Steve";
        vm.SelectedInstance = VanillaPack;

        await vm.LaunchCommand.ExecuteAsync();

        var begun = Assert.Single(playtime.Begun);
        Assert.Equal(("plain", 4242), (begun.InstanceId, begun.ProcessId));
    }

    private static LocalMod Mod(string file, bool enabled) => new()
    {
        Filename = file,
        DisplayName = file,
        IsEnabled = enabled,
    };

    internal sealed class FakePlaytimeStore : IPlaytimeStore
    {
        public Dictionary<string, TimeSpan> Totals { get; } = new();
        public List<PlaySession> Begun { get; } = new();

        public Task<TimeSpan> GetTotalAsync(string instanceId, CancellationToken cancellationToken)
            => Task.FromResult(Totals.GetValueOrDefault(instanceId));

        public Task BeginSessionAsync(PlaySession session, CancellationToken cancellationToken)
        {
            lock (Begun) Begun.Add(session);
            return Task.CompletedTask;
        }

        public Task<TimeSpan> EndSessionAsync(int processId, DateTimeOffset endedAt, CancellationToken cancellationToken)
            => Task.FromResult(TimeSpan.Zero);

        public Task<IReadOnlyList<PlaySession>> ListActiveAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlaySession>>(Array.Empty<PlaySession>());
    }

    internal sealed class FakeModManager : IInstanceModManager
    {
        public List<LocalMod> Mods { get; } = new();

        public Task<IReadOnlyList<LocalMod>> ListInstalledAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LocalMod>>(Mods.ToList());

        public Task InstallAsync(Instance instance, ModFile file, IModRepository repository, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveAsync(Instance instance, string filename, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetEnabledAsync(Instance instance, string filename, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
