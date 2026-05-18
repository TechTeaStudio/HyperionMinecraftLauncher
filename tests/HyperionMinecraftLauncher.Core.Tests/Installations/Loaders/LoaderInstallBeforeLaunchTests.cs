using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Installations.Loaders;

public class LoaderInstallBeforeLaunchTests
{
    [Fact]
    public async Task LaunchAsync_VanillaLoader_SkipsLoaderInstall_AndKeepsVersionName()
    {
        var underlying = new FakeUnderlyingLauncher { ProcessIdToReturn = 1 };
        var installer = new SpyLoaderInstaller();
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger(), modLoaderInstaller: installer);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var result = await service.LaunchAsync(
            new LaunchRequest { VersionName = "1.21.5", Session = auth, Loader = ModLoader.None },
            null, CancellationToken.None);

        Assert.Equal("1.21.5", result.VersionName);
        Assert.False(installer.WasCalled);
        Assert.Equal("1.21.5", underlying.LastInstalledVersionName);
    }

    [Fact]
    public async Task LaunchAsync_FabricLoader_InvokesInstallerAndUsesReturnedVersionName()
    {
        var underlying = new FakeUnderlyingLauncher { ProcessIdToReturn = 4242 };
        var installer = new SpyLoaderInstaller { Result = "fabric-loader-0.16.10-1.21.5" };
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger(), modLoaderInstaller: installer);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var result = await service.LaunchAsync(
            new LaunchRequest
            {
                VersionName = "1.21.5",
                Session = auth,
                Loader = ModLoader.Fabric,
                LoaderVersion = "0.16.10",
            },
            null, CancellationToken.None);

        Assert.True(installer.WasCalled);
        Assert.Equal(ModLoader.Fabric, installer.LastLoader);
        Assert.Equal("1.21.5", installer.LastMcVersion);
        Assert.Equal("0.16.10", installer.LastLoaderVersion);
        // The returned modded id should be what we ultimately install and launch.
        Assert.Equal("fabric-loader-0.16.10-1.21.5", result.VersionName);
        Assert.Equal("fabric-loader-0.16.10-1.21.5", underlying.LastInstalledVersionName);
    }

    [Fact]
    public async Task LaunchAsync_LoaderInstallerNotConfigured_NonVanilla_SkipsLoaderAndUsesVanilla()
    {
        // No modLoaderInstaller wired - the launcher service should treat the loader request as
        // metadata-only and proceed with the vanilla install path. This protects headless / CLI
        // contexts that don't have the CmlLib installer adapters available.
        var underlying = new FakeUnderlyingLauncher { ProcessIdToReturn = 99 };
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var result = await service.LaunchAsync(
            new LaunchRequest
            {
                VersionName = "1.21.5",
                Session = auth,
                Loader = ModLoader.Fabric,
                LoaderVersion = "0.16.10",
            },
            null, CancellationToken.None);

        Assert.Equal("1.21.5", result.VersionName);
        Assert.Equal("1.21.5", underlying.LastInstalledVersionName);
    }

    [Fact]
    public async Task LaunchAsync_LoaderInstallerThrowsNotSupported_WrappedAsInstallationFailed()
    {
        var underlying = new FakeUnderlyingLauncher();
        var installer = new SpyLoaderInstaller { ThrowOn = new NotSupportedException("not in this build") };
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger(), modLoaderInstaller: installer);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InstallationFailedException>(() =>
            service.LaunchAsync(
                new LaunchRequest { VersionName = "1.21.5", Session = auth, Loader = ModLoader.OptiFine },
                null, CancellationToken.None));

        Assert.IsType<NotSupportedException>(ex.InnerException);
        Assert.Contains("OptiFine", ex.Message);
    }

    [Fact]
    public async Task LaunchAsync_LoaderInstallerCancellation_Propagates()
    {
        var underlying = new FakeUnderlyingLauncher();
        var installer = new SpyLoaderInstaller { ThrowOn = new OperationCanceledException() };
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger(), modLoaderInstaller: installer);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LaunchAsync(
                new LaunchRequest { VersionName = "1.21.5", Session = auth, Loader = ModLoader.Forge },
                null, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_LoaderInstallReportsProgressInLauncherStream()
    {
        var underlying = new FakeUnderlyingLauncher();
        var installer = new SpyLoaderInstaller
        {
            Result = "1.20.4-forge-49.0.30",
            ProgressEventsToEmit = new[] { 0.25, 0.5, 0.75, 1.0 },
        };
        var service = new CmlLibMinecraftLauncherService(underlying, new RecordingLogger(), modLoaderInstaller: installer);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var reported = new List<LaunchProgress>();
        var progress = new SynchronousProgress<LaunchProgress>(reported.Add);

        _ = await service.LaunchAsync(
            new LaunchRequest
            {
                VersionName = "1.20.4",
                Session = auth,
                Loader = ModLoader.Forge,
                LoaderVersion = "49.0.30",
            },
            progress, CancellationToken.None);

        // The "Installing Forge 49.0.30" stage is emitted once with Fraction=0 and then re-emitted
        // for every fraction the installer reports (0.25, 0.5, 0.75, 1.0).
        Assert.Contains(reported, p => p.Stage.StartsWith("Installing Forge", StringComparison.Ordinal));
        Assert.Contains(reported, p => p.Fraction == 0.5);
        Assert.Contains(reported, p => p.Fraction == 1.0);
    }
}

internal sealed class SpyLoaderInstaller : IModLoaderInstaller
{
    public string Result { get; set; } = "<modded-id>";
    public Exception? ThrowOn { get; set; }
    public IReadOnlyList<double> ProgressEventsToEmit { get; set; } = Array.Empty<double>();

    public bool WasCalled { get; private set; }
    public ModLoader LastLoader { get; private set; }
    public string? LastMcVersion { get; private set; }
    public string? LastLoaderVersion { get; private set; }

    public Task<string> InstallAsync(ModLoader loader, string minecraftVersion, string? loaderVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        WasCalled = true;
        LastLoader = loader;
        LastMcVersion = minecraftVersion;
        LastLoaderVersion = loaderVersion;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var p in ProgressEventsToEmit) progress?.Report(p);
        if (ThrowOn is not null) throw ThrowOn;
        return Task.FromResult(Result);
    }
}
