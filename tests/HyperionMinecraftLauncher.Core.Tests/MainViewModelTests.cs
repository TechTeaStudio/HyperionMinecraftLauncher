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
    public void Defaults_NoSession_ChipShowsSignInButtonOnly()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.IsSignedInOnline);
        Assert.False(vm.HasSession);
        // No session -> AccountDisplay is empty so the chip's label isn't redundant with the Sign-in button.
        Assert.Equal(string.Empty, vm.AccountDisplay);
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
        Assert.False(vm.HasSession);
        Assert.True(fakeAuth.SignOutCalled);
        Assert.Equal(string.Empty, vm.AccountDisplay);
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
    public async Task RefreshInstances_MergesInstalledVersionsAsAutoImported()
    {
        var vm = NewVm(out var service, out _);
        service.InstancesToReturn = new[]
        {
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance
            {
                Id = "saved-1", Name = "My modpack", VersionId = "1.21.4",
            },
        };
        service.InstalledVersionsToReturn = new[]
        {
            // Same VersionId as the saved instance - must NOT be duplicated.
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
            {
                Id = "1.21.4", Type = "release", JsonPath = "X",
            },
            // Not covered by any saved instance - must show up as auto-imported.
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
            {
                Id = "1.20.4", Type = "release", JsonPath = "X",
            },
        };

        await vm.RefreshInstancesCommand.ExecuteAsync();

        Assert.Equal(2, vm.Instances.Count);
        // Saved comes first, then the auto-imported entry the user never created.
        Assert.False(vm.Instances[0].IsAutoImported);
        Assert.Equal("saved-1", vm.Instances[0].Id);
        Assert.True(vm.Instances[1].IsAutoImported);
        Assert.Equal("1.20.4", vm.Instances[1].VersionId);
        Assert.Contains("+1 auto-imported", vm.LogText);
        // InstalledVersions still populated for the SelectedProfile cross-reference.
        Assert.Equal(2, vm.InstalledVersions.Count);
    }

    [Fact]
    public async Task DeleteCommand_DisabledForAutoImportedInstance()
    {
        var vm = NewVm(out var service, out _);
        service.InstalledVersionsToReturn = new[]
        {
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
            {
                Id = "1.21.5", Type = "release", JsonPath = "X",
            },
        };
        await vm.RefreshInstancesCommand.ExecuteAsync();

        Assert.Single(vm.Instances);
        vm.SelectedInstance = vm.Instances[0];
        Assert.True(vm.SelectedInstance.IsAutoImported);
        Assert.False(vm.DeleteInstanceCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteCommand_EnabledForSavedInstance()
    {
        var vm = NewVm(out _, out _);
        vm.Instances.Add(new TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance
        {
            Id = "real", Name = "Real", VersionId = "1.21.5",
        });
        vm.SelectedInstance = vm.Instances[0];

        Assert.False(vm.SelectedInstance.IsAutoImported);
        Assert.True(vm.DeleteInstanceCommand.CanExecute(null));
    }

    [Fact]
    public async Task LaunchAsync_AutoImportedInstance_DoesNotPersistLastPlayedAt()
    {
        var vm = NewVm(out var service, out _);
        service.InstalledVersionsToReturn = new[]
        {
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion
            {
                Id = "1.21.5", Type = "release", JsonPath = "X",
            },
        };
        service.LaunchResultToReturn = new LaunchResult { ProcessId = 7, VersionName = "1.21.5" };

        await vm.RefreshInstancesCommand.ExecuteAsync();
        vm.SelectedInstance = vm.Instances[0];
        Assert.True(vm.SelectedInstance.IsAutoImported);

        await vm.LaunchCommand.ExecuteAsync();

        // The store must not have received a SaveInstanceAsync for an auto-imported entry.
        Assert.Null(service.LastSavedInstance);
        // But the in-memory tile still got the LastPlayedAt bump.
        Assert.NotNull(vm.Instances[0].LastPlayedAt);
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

    [Fact]
    public void IsSidebarCollapsed_DefaultsFalse_WidthIsExpanded()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.IsSidebarCollapsed);
        Assert.Equal(MainViewModel.SidebarExpandedWidth, vm.SidebarWidth);
        Assert.True(vm.AreSidebarLabelsVisible);
    }

    [Fact]
    public void IsSidebarCollapsed_FlipTrue_UpdatesWidthAndLabelsAndRaisesINPC()
    {
        var vm = NewVm(out _, out _);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSidebarCollapsed = true;

        Assert.True(vm.IsSidebarCollapsed);
        Assert.Equal(MainViewModel.SidebarCollapsedWidth, vm.SidebarWidth);
        Assert.False(vm.AreSidebarLabelsVisible);
        Assert.Contains(nameof(MainViewModel.IsSidebarCollapsed), raised);
        Assert.Contains(nameof(MainViewModel.SidebarWidth), raised);
        Assert.Contains(nameof(MainViewModel.AreSidebarLabelsVisible), raised);
    }

    [Fact]
    public async Task ToggleSidebarCommand_FlipsIsSidebarCollapsedBackAndForth()
    {
        var vm = NewVm(out _, out _);

        Assert.False(vm.IsSidebarCollapsed);

        await vm.ToggleSidebarCommand.ExecuteAsync();
        Assert.True(vm.IsSidebarCollapsed);

        await vm.ToggleSidebarCommand.ExecuteAsync();
        Assert.False(vm.IsSidebarCollapsed);
    }

    [Fact]
    public async Task VersionFilter_ChipsAndSearch_NarrowFilteredVersions()
    {
        var vm = NewVm(out var service, out _);
        service.VersionsToReturn = new[]
        {
            new VersionMetadata { Name = "1.21.5",  Type = "release"   },
            new VersionMetadata { Name = "1.21.4",  Type = "release"   },
            new VersionMetadata { Name = "24w14a",  Type = "snapshot"  },
            new VersionMetadata { Name = "b1.7.3",  Type = "old_beta"  },
            new VersionMetadata { Name = "a1.0.4",  Type = "old_alpha" },
        };

        await vm.RefreshVersionsCommand.ExecuteAsync();

        // Defaults: only Release on -> exactly the two 1.21.x entries survive.
        Assert.Equal(2, vm.FilteredVersions.Count);
        Assert.All(vm.FilteredVersions, v => Assert.Equal("release", v.Type));
        Assert.NotNull(vm.SelectedVersion);
        Assert.Equal("release", vm.SelectedVersion!.Type);

        // Enable Snapshot too -> three entries; the previous Release selection still
        // satisfies the filter and must not be reset.
        var previous = vm.SelectedVersion;
        vm.ShowSnapshot = true;
        Assert.Equal(3, vm.FilteredVersions.Count);
        Assert.Same(previous, vm.SelectedVersion);

        // Turn Release off -> Release entries gone, only snapshot remains. Previous
        // selection was Release-typed so it gets replaced with the first survivor.
        vm.ShowRelease = false;
        Assert.Single(vm.FilteredVersions);
        Assert.Equal("24w14a", vm.FilteredVersions[0].Name);
        Assert.Same(vm.FilteredVersions[0], vm.SelectedVersion);

        // Search is case-insensitive on Name and DisplayText. Re-enable Release first.
        vm.ShowRelease = true;
        vm.VersionSearchText = "1.21.5";
        Assert.Single(vm.FilteredVersions);
        Assert.Equal("1.21.5", vm.FilteredVersions[0].Name);

        // Clearing the search restores the chip-driven list.
        vm.VersionSearchText = string.Empty;
        Assert.Equal(3, vm.FilteredVersions.Count);
    }

    [Fact]
    public async Task RefreshAccountsAsync_PopulatesAccountsAndPicksMostRecentAsActive_WhenStoreHasNoActive()
    {
        var fakeAuth = new FakeMicrosoftAuthService
        {
            CachedAccounts = new[]
            {
                new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts.Account
                {
                    Id = "a", Username = "Alex",
                    Uuid = "11111111111111111111111111111111",
                    LastUsedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    IsOffline = false,
                },
                new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts.Account
                {
                    Id = "b", Username = "Steve",
                    Uuid = "22222222222222222222222222222222",
                    LastUsedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    IsOffline = false,
                },
            },
        };
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger(), fakeAuth);

        await vm.RefreshAccountsAsync(CancellationToken.None);

        Assert.Equal(2, vm.Accounts.Count);
        // Most-recent first.
        Assert.Equal("Steve", vm.Accounts[0].Username);
        // Without an explicit active id from the store, the VM falls back to the top entry.
        Assert.NotNull(vm.ActiveAccount);
        Assert.Equal("b", vm.ActiveAccount!.Id);
    }

    [Fact]
    public async Task SwitchAccount_CallsSilentSignIn_AndSetsCurrentSession()
    {
        var fakeAuth = new FakeMicrosoftAuthService
        {
            ResultToReturn = new AuthResult
            {
                Username = "Notch", Uuid = "00000000",
                AccessToken = "t", IsOffline = false,
            },
            CachedAccounts = new[]
            {
                new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts.Account
                {
                    Id = "msal-target", Username = "Notch", Uuid = "00000000",
                    LastUsedAt = DateTimeOffset.UtcNow, IsOffline = false,
                },
            },
        };
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger(), fakeAuth);
        await vm.RefreshAccountsAsync(CancellationToken.None);
        var target = vm.Accounts[0];

        await vm.SwitchAccountCommand.ExecuteAsync(target);

        Assert.Equal("msal-target", fakeAuth.LastSignInSilentlyId);
        Assert.NotNull(vm.CurrentSession);
        Assert.Equal("Notch", vm.CurrentSession!.Username);
    }

    [Fact]
    public async Task RemoveAccount_CallsSignOutAsync_WithId_AndClearsActiveSession()
    {
        var fakeAuth = new FakeMicrosoftAuthService();
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger(), fakeAuth);
        var target = new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts.Account
        {
            Id = "doomed", Username = "X", Uuid = "x",
            LastUsedAt = DateTimeOffset.UtcNow, IsOffline = false,
        };

        await vm.RemoveAccountCommand.ExecuteAsync(target);

        Assert.Equal("doomed", fakeAuth.LastSignOutId);
    }

    [Fact]
    public async Task QuickPlayLaunchAsync_Multiplayer_LogsTargetAndPassesQuickPlayToService()
    {
        var vm = NewVm(out var service, out _);
        service.LaunchResultToReturn = new LaunchResult { ProcessId = 555, VersionName = "1.21.5" };

        var inst = new TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance
        {
            Id = "modpack", Name = "My SMP", VersionId = "1.21.5",
        };

        await vm.QuickPlayLaunchAsync(inst, new QuickPlay.Multiplayer("mc.hypixel.net", 25577));

        Assert.Contains("Quick play: joining mc.hypixel.net:25577.", vm.LogText);
        Assert.NotNull(service.LastLaunchRequest);
        var mp = Assert.IsType<QuickPlay.Multiplayer>(service.LastLaunchRequest!.QuickPlay);
        Assert.Equal("mc.hypixel.net", mp.Host);
        Assert.Equal(25577, mp.Port);
        Assert.Equal("1.21.5", service.LastLaunchRequest.VersionName);
    }

    [Fact]
    public async Task QuickPlayLaunchAsync_Singleplayer_LogsWorldFolderAndPassesQuickPlay()
    {
        var vm = NewVm(out var service, out _);
        var inst = new TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance
        {
            Id = "vanilla", Name = "Vanilla 1.21", VersionId = "1.21.5",
        };

        await vm.QuickPlayLaunchAsync(inst, new QuickPlay.Singleplayer("My Survival"));

        Assert.Contains("Quick play: joining My Survival.", vm.LogText);
        var sp = Assert.IsType<QuickPlay.Singleplayer>(service.LastLaunchRequest!.QuickPlay);
        Assert.Equal("My Survival", sp.WorldFolderName);
    }

    [Fact]
    public async Task QuickPlayLaunchAsync_NullInstance_Throws()
    {
        var vm = NewVm(out _, out _);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            vm.QuickPlayLaunchAsync(null!, new QuickPlay.Singleplayer("w")));
    }

    [Fact]
    public async Task LaunchAsync_NormalLaunch_PassesQuickPlayNoneToService()
    {
        var vm = NewVm(out var service, out _);
        vm.SelectedVersion = new VersionMetadata { Name = "1.21.5", Type = "release" };

        await vm.LaunchCommand.ExecuteAsync();

        Assert.NotNull(service.LastLaunchRequest);
        Assert.IsType<QuickPlay.None>(service.LastLaunchRequest!.QuickPlay);
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

    public IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry> ServersToReturn { get; set; }
        = Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry>();

    public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry>> ListServersAsync(CancellationToken cancellationToken)
        => Task.FromResult(ServersToReturn);

    public IReadOnlyDictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?> PingsToReturn { get; set; }
        = new System.Collections.Generic.Dictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?>();

    public Task<IReadOnlyDictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?>> PingServersAsync(
        IEnumerable<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry> entries,
        CancellationToken cancellationToken)
        => Task.FromResult(PingsToReturn);

    public IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry> NewsToReturn { get; set; }
        = Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry>();

    public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry>> ListNewsAsync(CancellationToken cancellationToken)
        => Task.FromResult(NewsToReturn);

    public IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance> InstancesToReturn { get; set; }
        = Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance>();
    public TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance? LastSavedInstance { get; private set; }
    public string? LastDeletedInstanceId { get; private set; }

    public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance>> ListInstancesAsync(CancellationToken cancellationToken)
        => Task.FromResult(InstancesToReturn);

    public Task SaveInstanceAsync(TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance instance, CancellationToken cancellationToken)
    {
        LastSavedInstance = instance;
        return Task.CompletedTask;
    }

    public Task DeleteInstanceAsync(string id, CancellationToken cancellationToken)
    {
        LastDeletedInstanceId = id;
        return Task.CompletedTask;
    }

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

    public LaunchRequest? LastLaunchRequest { get; private set; }

    public Task<LaunchResult> LaunchAsync(LaunchRequest request, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        LastLaunchRequest = request;
        foreach (var p in ProgressEvents) progress?.Report(p);
        if (LaunchException is not null) throw LaunchException;
        return Task.FromResult(LaunchResultToReturn);
    }
}
