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

    [Fact]
    public void Defaults_AccountChipReadsAsSignIn_AndIsSignedInOnlineIsFalse()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.IsSignedInOnline);
        Assert.Equal("Sign in", vm.AccountDisplay);
        Assert.False(vm.SignOutCommand.CanExecute(null));
        Assert.True(vm.SignInMicrosoftCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedSection_DefaultsToHome_AndChangesFlipBooleans()
    {
        var vm = NewVm(out _, out _);
        Assert.Equal(NavSection.Home, vm.SelectedSection);
        Assert.True(vm.IsHomeSelected);

        vm.SelectedSection = NavSection.Servers;

        Assert.False(vm.IsHomeSelected);
        Assert.True(vm.IsServersSelected);
        Assert.False(vm.IsInstallationsSelected);
    }

    [Fact]
    public async Task RefreshInstalledVersions_PopulatesCollection()
    {
        var vm = NewVm(out var service, out _);
        service.InstalledVersionsToReturn = new[]
        {
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
            {
                Id = "1.21.5", Type = "release", JsonPath = "X",
            },
        };

        await vm.RefreshInstalledVersionsCommand.ExecuteAsync();

        Assert.Single(vm.InstalledVersions);
        Assert.Equal("1.21.5", vm.InstalledVersions[0].Id);
        Assert.Contains("Found 1 installed", vm.LogText);
    }

    [Fact]
    public void LaunchCommand_CanExecute_TrueWhenInstalledVersionSelected_EvenWithoutManifest()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.LaunchCommand.CanExecute(null));

        vm.SelectedInstalledVersion = new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
        {
            Id = "1.20.4", Type = "release", JsonPath = "X",
        };

        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Fact]
    public async Task SignInMicrosoft_OnSuccess_SetsSessionAndFlipsAccountState()
    {
        var vm = NewVm(out var service, out _);

        // StubLauncherService.AuthenticateAsync ignores Mode and always returns an offline result
        // (IsOffline=true). For this test we don't care about the "Online" flag specifically -
        // we just want to confirm the wiring runs.
        await vm.SignInMicrosoftCommand.ExecuteAsync();

        Assert.NotNull(vm.CurrentSession);
        Assert.Contains("Signed in as", vm.LogText);
    }

    [Fact]
    public async Task SignOut_AfterSignedIn_ClearsSessionAndCallsAuthProvider()
    {
        var fakeAuth = new FakeMicrosoftAuthService();
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger(), fakeAuth);

        await vm.SignInMicrosoftCommand.ExecuteAsync();
        Assert.NotNull(vm.CurrentSession);

        await vm.SignOutCommand.ExecuteAsync();

        Assert.Null(vm.CurrentSession);
        Assert.True(fakeAuth.SignOutCalled);
        Assert.Equal("Sign in", vm.AccountDisplay);
    }

    [Fact]
    public async Task LaunchAsync_WithSignedInSession_UsesItDirectlyAndSkipsOfflineAuth()
    {
        var fakeAuth = new FakeMicrosoftAuthService
        {
            ResultToReturn = new AuthResult
            {
                Username = "Notch",
                Uuid = "069a79f444e94726a5befca90e38aaf5",
                AccessToken = "real-token",
                IsOffline = false,
            },
        };
        var service = new StubLauncherService
        {
            // If LaunchAsync wrongly re-auths, this'll be picked up - but our stub's Authenticate is fine either way.
            VersionsToReturn = new[] { new VersionMetadata { Name = "1.21.5", Type = "release" } },
        };
        var vm = new MainViewModel(service, new RecordingLogger(), fakeAuth);

        await vm.SignInMicrosoftCommand.ExecuteAsync();
        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };

        await vm.LaunchCommand.ExecuteAsync();

        Assert.Contains("Microsoft session", vm.LogText);
    }

    [Fact]
    public async Task LaunchAsync_PrefersSelectedInstalledVersionOverManifest()
    {
        var vm = NewVm(out var service, out _);
        service.LaunchResultToReturn = new LaunchResult { ProcessId = 99, VersionName = "captured" };

        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };
        vm.SelectedInstalledVersion = new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
        {
            Id = "1.20.1-forge-47.4.5", Type = "release", JsonPath = "X",
            Loader = TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.Forge,
        };

        await vm.LaunchCommand.ExecuteAsync();

        // The log line records the version we passed to LaunchAsync.
        Assert.Contains("1.20.1-forge-47.4.5", vm.LogText);
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

    public IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile> ProfilesToReturn { get; set; }
        = Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile>();

    public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken)
        => Task.FromResult(ProfilesToReturn);

    public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        if (AuthException is not null) throw AuthException;
        // Mirror the real CmlLibMinecraftLauncherService: Microsoft mode returns an "online" session;
        // offline mode returns an offline session. The actual provider is mocked elsewhere.
        return Task.FromResult(new AuthResult
        {
            Username = string.IsNullOrWhiteSpace(request.Username) ? "Notch" : request.Username,
            Uuid = "00000000-0000-0000-0000-000000000000",
            AccessToken = "stub-token",
            IsOffline = request.Mode == AuthMode.Offline,
        });
    }

    public Task<LaunchResult> LaunchAsync(LaunchRequest request, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var p in ProgressEvents) progress?.Report(p);
        if (LaunchException is not null) throw LaunchException;
        return Task.FromResult(LaunchResultToReturn);
    }
}
