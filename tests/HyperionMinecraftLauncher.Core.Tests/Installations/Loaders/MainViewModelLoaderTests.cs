using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

// Reuse the in-file stubs from MainViewModelTests so we don't double-implement
// the very wide IMinecraftLauncherService surface here.
using CoreTests = TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Installations.Loaders;

public class MainViewModelLoaderTests
{
    [Fact]
    public async Task ListLoaderVersionsAsync_NoFetcherConfigured_ReturnsEmpty()
    {
        var vm = NewVm(out _, fetcher: null);
        var result = await vm.ListLoaderVersionsAsync(ModLoader.Fabric, "1.21.5", CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_Vanilla_ReturnsEmpty()
    {
        var fetcher = new CapturingFetcher { Result = new[] { "0.16.10" } };
        var vm = NewVm(out _, fetcher);

        var result = await vm.ListLoaderVersionsAsync(ModLoader.None, "1.21.5", CancellationToken.None);

        Assert.Empty(result);
        Assert.False(fetcher.WasCalled);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_EmptyVersion_ReturnsEmpty()
    {
        var fetcher = new CapturingFetcher { Result = new[] { "0.16.10" } };
        var vm = NewVm(out _, fetcher);

        var result = await vm.ListLoaderVersionsAsync(ModLoader.Fabric, "", CancellationToken.None);

        Assert.Empty(result);
        Assert.False(fetcher.WasCalled);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_Fabric_DispatchesAndReturnsResult()
    {
        var fetcher = new CapturingFetcher { Result = new[] { "0.16.10", "0.16.9" } };
        var vm = NewVm(out _, fetcher);

        var result = await vm.ListLoaderVersionsAsync(ModLoader.Fabric, "1.21.5", CancellationToken.None);

        Assert.Equal(new[] { "0.16.10", "0.16.9" }, result);
        Assert.Equal(ModLoader.Fabric, fetcher.LastLoader);
        Assert.Equal("1.21.5", fetcher.LastMcVersion);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_FetcherThrows_LogsAndReturnsEmpty()
    {
        var fetcher = new CapturingFetcher { ThrowOn = new InvalidOperationException("meta API down") };
        var vm = NewVm(out var logger, fetcher);

        var result = await vm.ListLoaderVersionsAsync(ModLoader.Forge, "1.20.4", CancellationToken.None);

        Assert.Empty(result);
        Assert.Contains(logger.WarnEntries, w => w.Contains("Could not list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_FetcherCancelled_Propagates()
    {
        var fetcher = new CapturingFetcher { ThrowOn = new OperationCanceledException() };
        var vm = NewVm(out _, fetcher);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            vm.ListLoaderVersionsAsync(ModLoader.Forge, "1.20.4", CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstanceAsync_RecordsLoaderAndVersion()
    {
        var vm = NewVm(out _, fetcher: null);

        var inst = await vm.CreateInstanceAsync("Modded", "1.21.5", InstanceIcons.GrassBlock,
            ModLoader.Fabric, "0.16.10");

        Assert.Equal(ModLoader.Fabric, inst.Loader);
        Assert.Equal("0.16.10", inst.LoaderVersion);
    }

    [Fact]
    public async Task CreateInstanceAsync_VanillaDropsLoaderVersionEvenIfSupplied()
    {
        var vm = NewVm(out _, fetcher: null);

        var inst = await vm.CreateInstanceAsync("Plain", "1.21.5", InstanceIcons.GrassBlock,
            ModLoader.None, "should-be-ignored");

        Assert.Equal(ModLoader.None, inst.Loader);
        Assert.Null(inst.LoaderVersion);
    }

    private static MainViewModel NewVm(out RecordingLogger logger, IModLoaderVersionFetcher? fetcher)
    {
        logger = new RecordingLogger();
        // StubLauncherService lives in MainViewModelTests.cs at the test-project root namespace,
        // so we reference it via the using alias above.
        var service = new CoreTests.StubLauncherService();
        return new MainViewModel(service, logger, modLoaderVersionFetcher: fetcher);
    }

    private sealed class CapturingFetcher : IModLoaderVersionFetcher
    {
        public IReadOnlyList<string> Result { get; set; } = Array.Empty<string>();
        public Exception? ThrowOn { get; set; }
        public bool WasCalled { get; private set; }
        public ModLoader LastLoader { get; private set; }
        public string? LastMcVersion { get; private set; }

        public Task<IReadOnlyList<string>> ListLoaderVersionsAsync(ModLoader loader, string minecraftVersion, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastLoader = loader;
            LastMcVersion = minecraftVersion;
            if (ThrowOn is not null) throw ThrowOn;
            return Task.FromResult(Result);
        }
    }
}
