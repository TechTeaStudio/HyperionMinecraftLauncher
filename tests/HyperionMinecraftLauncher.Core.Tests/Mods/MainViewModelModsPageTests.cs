using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods;

public class MainViewModelModsPageTests
{
    private static readonly Instance Nestle = new()
    {
        Id = "nestle", Name = "Nestle", VersionId = "26.1.2", Loader = ModLoader.Fabric, LoaderVersion = "0.19.3",
    };
    private static readonly Instance Other = new() { Id = "other", Name = "Other", VersionId = "1.21.5" };

    private static MainViewModel NewVm(ModsByInstance mods, RecordingRepository? modrinth = null)
        => new(new StubLauncherService(), new RecordingLogger(),
            instanceModManager: mods, modrinthRepository: modrinth ?? new RecordingRepository());

    [Fact]
    public async Task OpeningModsPage_LoadsPopularModsForTheSelectedInstance()
    {
        var modrinth = new RecordingRepository();
        var vm = NewVm(new ModsByInstance(), modrinth);
        vm.SelectedInstance = Nestle;

        vm.SelectedSection = NavSection.Mods;

        await Eventually(() => vm.ModSearchResults.Count == 2);
        var q = Assert.Single(modrinth.Queries);
        Assert.Equal(("", "26.1.2", ModLoader.Fabric), (q.Query, q.GameVersion, q.Loader));
    }

    [Fact]
    public async Task ReopeningModsPage_WithNothingChanged_DoesNotQueryAgain()
    {
        var modrinth = new RecordingRepository();
        var vm = NewVm(new ModsByInstance(), modrinth);
        vm.SelectedInstance = Nestle;
        vm.SelectedSection = NavSection.Mods;
        await Eventually(() => vm.ModSearchResults.Count == 2);

        vm.SelectedSection = NavSection.Home;
        vm.SelectedSection = NavSection.Mods;
        await Task.Delay(100);

        Assert.Single(modrinth.Queries);
    }

    [Fact]
    public async Task SwitchingInstance_OnModsPage_RequeriesWithTheNewFilters()
    {
        var modrinth = new RecordingRepository();
        var vm = NewVm(new ModsByInstance(), modrinth);
        vm.SelectedInstance = Nestle;
        vm.SelectedSection = NavSection.Mods;
        await Eventually(() => modrinth.Queries.Count == 1);

        vm.SelectedInstance = Other;

        await Eventually(() => modrinth.Queries.Count == 2);
        Assert.Equal("1.21.5", modrinth.Queries[1].GameVersion);
    }

    [Fact]
    public async Task OpeningModsPage_LoadsTheSelectedInstancesMods()
    {
        var mods = new ModsByInstance { ["nestle"] = new[] { "sodium.jar", "lithium.jar" } };
        var vm = NewVm(mods);
        vm.SelectedInstance = Nestle;

        vm.SelectedSection = NavSection.Mods;

        await Eventually(() => vm.InstalledMods.Count == 2);
        Assert.Equal(new[] { "sodium.jar", "lithium.jar" }, vm.InstalledMods.Select(m => m.Filename));
    }

    [Fact]
    public async Task SwitchingInstance_OnModsPage_ReloadsTheList()
    {
        var mods = new ModsByInstance
        {
            ["nestle"] = new[] { "sodium.jar", "lithium.jar" },
            ["other"] = new[] { "jei.jar" },
        };
        var vm = NewVm(mods);
        vm.SelectedInstance = Nestle;
        vm.SelectedSection = NavSection.Mods;
        await Eventually(() => vm.InstalledMods.Count == 2);

        vm.SelectedInstance = Other;

        await Eventually(() => vm.InstalledMods.Select(m => m.Filename).SequenceEqual(new[] { "jei.jar" }));
    }

    [Fact]
    public async Task OpeningModsPage_WithNothingSelected_PicksTheMostRecentInstance()
    {
        var mods = new ModsByInstance { ["nestle"] = new[] { "sodium.jar" } };
        var vm = NewVm(mods);
        vm.Instances.Add(Nestle);
        vm.Instances.Add(Other);

        vm.SelectedSection = NavSection.Mods;

        Assert.Same(Nestle, vm.SelectedInstance);
        await Eventually(() => vm.InstalledMods.Count == 1);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("condition not met within 3 s");
            await Task.Delay(20);
        }
    }

    /// <summary>Modrinth stand-in: records every query and answers with two mods.</summary>
    private sealed class RecordingRepository : IModRepository
    {
        public List<ModSearchQuery> Queries { get; } = new();

        public ModSource Source => ModSource.Modrinth;

        public Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
        {
            lock (Queries) Queries.Add(query);
            return Task.FromResult<IReadOnlyList<Mod>>(new[]
            {
                new Mod { Id = "AANobbMI", Slug = "sodium", Name = "Sodium", Source = ModSource.Modrinth },
                new Mod { Id = "gvQqBUqZ", Slug = "lithium", Name = "Lithium", Source = ModSource.Modrinth },
            });
        }

        public Task<IReadOnlyList<ModFile>> ListFilesAsync(string modId, string? gameVersion, ModLoader? loader, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ModFile>>(Array.Empty<ModFile>());

        public Task DownloadAsync(ModFile file, System.IO.Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>Mod manager backed by a filename list per instance id.</summary>
    private sealed class ModsByInstance : Dictionary<string, string[]>, IInstanceModManager
    {
        public Task<IReadOnlyList<LocalMod>> ListInstalledAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LocalMod>>((TryGetValue(instance.Id, out var files) ? files : Array.Empty<string>())
                .Select(f => new LocalMod { Filename = f, DisplayName = f, IsEnabled = true })
                .ToList());

        public Task InstallAsync(Instance instance, ModFile file, IModRepository repository, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveAsync(Instance instance, string filename, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetEnabledAsync(Instance instance, string filename, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
