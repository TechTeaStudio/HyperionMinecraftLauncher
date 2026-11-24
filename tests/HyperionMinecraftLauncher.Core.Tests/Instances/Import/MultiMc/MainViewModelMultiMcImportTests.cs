using System;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Playtime;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Import.MultiMc;

/// <summary>
/// The "Import from MultiMC" flow of <see cref="MainViewModel"/>: the picked mode reaches the
/// importer, the instance is saved, and the time played in MultiMC is credited to it.
/// </summary>
public class MainViewModelMultiMcImportTests
{
    private static readonly Instance Pepeland = new()
    {
        Id = "ppl11",
        Name = "PEPELAND 11 26.3",
        VersionId = "26.3",
        Loader = ModLoader.Fabric,
        LoaderVersion = "0.20.1",
        GameDirectory = @"D:\MultiMC\instances\PEPELAND 10 26.3\.minecraft",
    };

    [Fact]
    public async Task LinkPick_SavesLinkedInstance_AndCreditsMultiMcPlaytime()
    {
        var importer = new FakeImporter
        {
            Result = new MultiMcImportResult { Instance = Pepeland, PlayedInSource = TimeSpan.FromSeconds(253012) },
        };
        var playtime = new MainViewModelInstanceCardTests.FakePlaytimeStore();
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger(), multiMcImporter: importer, playtimeStore: playtime);
        vm.SetMultiMcImportPickRequest(_ => Task.FromResult<MultiMcImportPick?>(
            new MultiMcImportPick(@"D:\MultiMC\instances\PEPELAND 10 26.3", MultiMcImportMode.Link)));

        await vm.ImportMultiMcAsync();

        Assert.Equal(MultiMcImportMode.Link, importer.Mode);
        Assert.Equal(@"D:\MultiMC\instances\PEPELAND 10 26.3", importer.Source);
        Assert.Equal(Pepeland, service.LastSavedInstance);
        Assert.Equal(TimeSpan.FromSeconds(253012), playtime.Totals["ppl11"]);
    }

    [Fact]
    public async Task CancelledPick_ImportsNothing()
    {
        var importer = new FakeImporter();
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger(), multiMcImporter: importer,
            playtimeStore: new MainViewModelInstanceCardTests.FakePlaytimeStore());
        vm.SetMultiMcImportPickRequest(_ => Task.FromResult<MultiMcImportPick?>(null));

        await vm.ImportMultiMcAsync();

        Assert.Null(importer.Source);
        Assert.Null(service.LastSavedInstance);
    }

    private sealed class FakeImporter : IMultiMcInstanceImporter
    {
        public MultiMcImportResult? Result { get; init; }
        public string? Source { get; private set; }
        public MultiMcImportMode? Mode { get; private set; }

        public Task<MultiMcImportResult> ImportAsync(
            string sourceZipOrFolderPath,
            MultiMcImportMode mode,
            string? overrideName,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            Source = sourceZipOrFolderPath;
            Mode = mode;
            return Task.FromResult(Result ?? throw new InvalidOperationException("no result set"));
        }
    }
}
