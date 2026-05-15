using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_DefaultsAreSensible()
    {
        var vm = NewVm(out _, out _);

        Assert.Equal("Steve", vm.Username);
        Assert.Empty(vm.AvailableVersions);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.SelectedVersion);
    }

    [Fact]
    public async Task RefreshVersions_PopulatesAvailableVersions()
    {
        var vm = NewVm(out var service, out _);
        service.VersionsToReturn = new[]
        {
            new VersionMetadata { Name = "1.21.5", Type = "release", ReleaseTime = DateTimeOffset.UtcNow },
            new VersionMetadata { Name = "1.21.4", Type = "release", ReleaseTime = DateTimeOffset.UtcNow.AddDays(-30) },
            new VersionMetadata { Name = "24w14a",  Type = "snapshot", ReleaseTime = DateTimeOffset.UtcNow.AddDays(-1) },
        };

        await vm.RefreshVersionsCommand.ExecuteAsync();

        Assert.Equal(3, vm.AvailableVersions.Count);
        Assert.Equal("1.21.5", vm.AvailableVersions[0].Name);
        Assert.Contains("Loaded 3 versions", vm.LogText);
    }

    [Fact]
    public void LaunchCommand_CanExecute_FalseWhenNoVersion_TrueOnceSelected()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.LaunchCommand.CanExecute(null));

        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Fact]
    public async Task LaunchCommand_HappyPath_AppendsProgressAndFinishes()
    {
        var vm = NewVm(out var service, out _);
        service.ProgressEvents = new[]
        {
            new LaunchProgress { Stage = "Downloading library", Fraction = 0.3, CurrentItem = "lwjgl.jar" },
            new LaunchProgress { Stage = "Install done", Fraction = 1.0 },
        };
        service.LaunchResultToReturn = new LaunchResult { ProcessId = 9999, VersionName = "1.21.5" };

        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };

        await vm.LaunchCommand.ExecuteAsync();

        Assert.False(vm.IsBusy);
        Assert.Contains("Authenticating", vm.LogText);
        Assert.Contains("Downloading library", vm.LogText);
        Assert.Contains("Launched. pid=9999", vm.LogText);
    }

    [Fact]
    public async Task LaunchCommand_LauncherException_SurfacedAsErrorLine_NoThrow()
    {
        var vm = NewVm(out var service, out _);
        service.LaunchException = new InstallationFailedException("disk full");
        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };

        await vm.LaunchCommand.ExecuteAsync();

        Assert.Contains("[error] disk full", vm.LogText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LaunchCommand_AuthenticationFailure_SurfacedAsErrorLine()
    {
        var vm = NewVm(out var service, out _);
        service.AuthException = new AuthenticationFailedException("not implemented");
        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };

        await vm.LaunchCommand.ExecuteAsync();

        Assert.Contains("[error] not implemented", vm.LogText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RefreshVersions_ServiceFailure_SurfacedAsErrorLine()
    {
        var vm = NewVm(out var service, out _);
        service.VersionsException = new InstallationFailedException("offline");

        await vm.RefreshVersionsCommand.ExecuteAsync();

        Assert.Contains("[error] offline", vm.LogText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task IsBusy_FlipsTrueDuringExecution_FalseAfter()
    {
        var vm = NewVm(out var service, out _);
        var snapshots = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy))
                snapshots.Add(vm.IsBusy);
        };

        service.VersionsToReturn = new[] { new VersionMetadata { Name = "1.21.5", Type = "release" } };

        await vm.RefreshVersionsCommand.ExecuteAsync();

        Assert.Contains(true, snapshots);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Append_FormatsWithUtcTimestampAndPreservesPriorContent()
    {
        var vm = NewVm(out _, out _);

        vm.Append("first");
        vm.Append("second");

        var lines = vm.LogText.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
    }

    private static MainViewModel NewVm(out StubLauncherService service, out RecordingLogger logger)
    {
        service = new StubLauncherService();
        logger = new RecordingLogger();
        return new MainViewModel(service, logger);
    }
}

internal sealed class StubLauncherService : IMinecraftLauncherService
{
    public IReadOnlyList<VersionMetadata> VersionsToReturn { get; set; } = Array.Empty<VersionMetadata>();
    public IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion> InstalledVersionsToReturn { get; set; }
        = Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion>();
    public Exception? VersionsException { get; set; }
    public Exception? AuthException { get; set; }
    public Exception? LaunchException { get; set; }
    public LaunchResult LaunchResultToReturn { get; set; } = new() { ProcessId = 1, VersionName = "stub" };
    public IReadOnlyList<LaunchProgress> ProgressEvents { get; set; } = Array.Empty<LaunchProgress>();

    public Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        if (VersionsException is not null) throw VersionsException;
        return Task.FromResult(VersionsToReturn);
    }

    public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken)
        => Task.FromResult(InstalledVersionsToReturn);

    public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        if (AuthException is not null) throw AuthException;
        return Task.FromResult(new AuthResult
        {
            Username = request.Username,
            Uuid = "00000000-0000-0000-0000-000000000000",
            AccessToken = "stub-token",
            IsOffline = true,
        });
    }

    public Task<LaunchResult> LaunchAsync(LaunchRequest request, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var p in ProgressEvents) progress?.Report(p);
        if (LaunchException is not null) throw LaunchException;
        return Task.FromResult(LaunchResultToReturn);
    }
}
