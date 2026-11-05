using System;
using System.Globalization;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

/// <summary>Home page behaviour: a ready-to-play instance, the new-instance shortcut and the latest-news panel.</summary>
public class HomePageTests
{
    public HomePageTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    [Fact]
    public async Task RefreshInstances_SelectsTheMostRecentlyPlayedInstance()
    {
        var service = new StubLauncherService
        {
            InstancesToReturn = new[]
            {
                new Instance { Id = "old", Name = "Old", VersionId = "1.21.8", LastPlayedAt = DateTimeOffset.UtcNow.AddDays(-3) },
                new Instance { Id = "pepe", Name = "PEPELAND", VersionId = "26.3", LastPlayedAt = DateTimeOffset.UtcNow.AddHours(-1) },
                new Instance { Id = "never", Name = "Never", VersionId = "26.1.2" },
            },
        };
        var vm = new MainViewModel(service, new RecordingLogger());

        await vm.RefreshInstancesCommand.ExecuteAsync();

        Assert.Equal("pepe", vm.SelectedInstance?.Id);
    }

    [Fact]
    public async Task RefreshInstances_NothingPlayedYet_SelectsTheFirstInstance()
    {
        var service = new StubLauncherService
        {
            InstancesToReturn = new[] { new Instance { Id = "a", Name = "A", VersionId = "26.3" }, new Instance { Id = "b", Name = "B", VersionId = "26.3" } },
        };
        var vm = new MainViewModel(service, new RecordingLogger());

        await vm.RefreshInstancesCommand.ExecuteAsync();

        Assert.Equal("a", vm.SelectedInstance?.Id);
    }

    [Fact]
    public async Task CreateInstance_SelectsTheNewInstanceSoItIsReadyToPlay()
    {
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger());

        var created = await vm.CreateInstanceAsync("Fresh", "26.3", InstanceIcons.GrassBlock);

        Assert.Same(created, vm.SelectedInstance);
    }

    [Fact]
    public async Task LatestNews_IsTheNewestArticleAndUpdatesWithTheFeed()
    {
        var service = new StubLauncherService
        {
            NewsToReturn = new[]
            {
                new NewsEntry { Title = "Minecraft 26.4 Snapshot 7", Category = "Minecraft: Java Edition", Date = "2026-11-03", Text = "" },
                new NewsEntry { Title = "Older", Category = "Minecraft", Date = "2026-10-30", Text = "" },
            },
        };
        var vm = new MainViewModel(service, new RecordingLogger());
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(MainViewModel.LatestNews);

        Assert.Null(vm.LatestNews);
        await vm.RefreshNewsCommand.ExecuteAsync();

        Assert.Equal("Minecraft 26.4 Snapshot 7", vm.LatestNews?.Title);
        Assert.True(raised);
    }
}
