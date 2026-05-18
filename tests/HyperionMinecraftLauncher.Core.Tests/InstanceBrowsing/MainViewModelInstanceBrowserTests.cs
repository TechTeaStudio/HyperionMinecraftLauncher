using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>Verifies the MainViewModel auto-refreshes the per-instance browser when SelectedInstance changes.</summary>
public class MainViewModelInstanceBrowserTests
{
    [Fact]
    public async Task SelectingAnInstance_TriggersBrowserRefresh()
    {
        var browser = new FakeInstanceBrowser();
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger(),
            microsoftAuth: null, settingsStore: null, instanceBrowser: browser);

        vm.SelectedInstance = new Instance { Id = "a", Name = "Alpha", VersionId = "1.21.5", GameDirectory = "/tmp/a" };

        // SelectedInstance setter fires fire-and-forget refresh - let it run.
        await browser.ScreenshotsCallTcs.Task;
        await browser.WorldsCallTcs.Task;
        await browser.ServersCallTcs.Task;

        Assert.Equal(1, browser.ScreenshotCalls);
        Assert.Equal(1, browser.WorldCalls);
        Assert.Equal(1, browser.ServerCalls);
    }

    [Fact]
    public async Task RefreshInstanceScreenshotsCommand_Disabled_WhenNoInstanceSelected()
    {
        var browser = new FakeInstanceBrowser();
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger(),
            microsoftAuth: null, settingsStore: null, instanceBrowser: browser);

        Assert.False(vm.RefreshInstanceScreenshotsCommand.CanExecute(null));

        vm.SelectedInstance = new Instance { Id = "x", Name = "X", VersionId = "1.21.5" };
        Assert.True(vm.RefreshInstanceScreenshotsCommand.CanExecute(null));

        await browser.ScreenshotsCallTcs.Task;
    }

    private sealed class FakeInstanceBrowser : IInstanceBrowser
    {
        public int ScreenshotCalls;
        public int WorldCalls;
        public int ServerCalls;
        public readonly TaskCompletionSource ScreenshotsCallTcs = new();
        public readonly TaskCompletionSource WorldsCallTcs = new();
        public readonly TaskCompletionSource ServersCallTcs = new();

        public Task<IReadOnlyList<ScreenshotEntry>> ListScreenshotsAsync(Instance instance, CancellationToken cancellationToken)
        {
            ScreenshotCalls++;
            ScreenshotsCallTcs.TrySetResult();
            return Task.FromResult<IReadOnlyList<ScreenshotEntry>>(System.Array.Empty<ScreenshotEntry>());
        }

        public Task<IReadOnlyList<WorldEntry>> ListWorldsAsync(Instance instance, CancellationToken cancellationToken)
        {
            WorldCalls++;
            WorldsCallTcs.TrySetResult();
            return Task.FromResult<IReadOnlyList<WorldEntry>>(System.Array.Empty<WorldEntry>());
        }

        public Task<IReadOnlyList<ServerListEntry>> ListServersAsync(Instance instance, CancellationToken cancellationToken)
        {
            ServerCalls++;
            ServersCallTcs.TrySetResult();
            return Task.FromResult<IReadOnlyList<ServerListEntry>>(System.Array.Empty<ServerListEntry>());
        }

        public Task<IReadOnlyList<ResourcePackEntry>> ListResourcePacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ResourcePackEntry>>(System.Array.Empty<ResourcePackEntry>());

        public Task<IReadOnlyList<ShaderPackEntry>> ListShaderPacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ShaderPackEntry>>(System.Array.Empty<ShaderPackEntry>());

        public Task<IReadOnlyList<DataPackEntry>> ListDataPacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DataPackEntry>>(System.Array.Empty<DataPackEntry>());
    }
}
