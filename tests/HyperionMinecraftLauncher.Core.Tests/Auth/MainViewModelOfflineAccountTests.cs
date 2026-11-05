using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Auth;

/// <summary>
/// The offline account lives in the account list like any other account: there is always one,
/// it is the active account until the player signs in with Microsoft, and its name is what an
/// offline launch uses.
/// </summary>
public class MainViewModelOfflineAccountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hypmcl-offline-" + Guid.NewGuid().ToString("N"));

    public MainViewModelOfflineAccountTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task RefreshAccounts_WithNoAccounts_CreatesADefaultOfflineAccountAndMakesItActive()
    {
        var store = NewStore();
        var vm = NewVm(store, out _);

        await vm.RefreshAccountsAsync(CancellationToken.None);

        var row = Assert.Single(vm.Accounts);
        Assert.True(row.IsOffline);
        Assert.Equal("Steve", row.Username);
        Assert.Equal(row.Id, vm.ActiveAccount!.Id);
        Assert.Equal("Steve", vm.Username);
        Assert.Contains(await store.ListAsync(CancellationToken.None), a => a.IsOffline && a.Username == "Steve");
        Assert.Equal("Steve (offline)", vm.AccountChipLabel);
    }

    [Fact]
    public async Task AddOfflineAccount_SavesItSwitchesToItAndLaunchesWithItsName()
    {
        var store = NewStore();
        var vm = NewVm(store, out var service);
        await vm.RefreshAccountsAsync(CancellationToken.None);

        await vm.AddOfflineAccountCommand.ExecuteAsync("Pepe_Fan");

        Assert.Equal("Pepe_Fan", vm.ActiveAccount!.Username);
        Assert.True(vm.ActiveAccount.IsOffline);
        Assert.Null(vm.CurrentSession);
        Assert.Contains(vm.Accounts, a => a.Username == "Pepe_Fan");
        Assert.Equal("Pepe_Fan", (await store.GetActiveAsync(CancellationToken.None))!.Username);

        vm.SelectedVersion = new VersionMetadata { Name = "26.3", Type = "release" };
        await vm.LaunchCommand.ExecuteAsync();
        Assert.Equal("Pepe_Fan", service.LastLaunchRequest!.Session.Username);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("seventeen_chars_x")]
    [InlineData("кириллица")]
    public async Task AddOfflineAccount_RejectsNamesMinecraftWouldNotAccept(string name)
    {
        var store = NewStore();
        var vm = NewVm(store, out _);
        await vm.RefreshAccountsAsync(CancellationToken.None);

        await vm.AddOfflineAccountCommand.ExecuteAsync(name);

        Assert.DoesNotContain(vm.Accounts, a => a.Username == name);
        Assert.Contains("[error]", vm.LogText);
    }

    [Fact]
    public async Task RemoveAccount_Offline_RemovesItFromTheStoreAndFallsBackToAnotherOfflineAccount()
    {
        var store = NewStore();
        var vm = NewVm(store, out _);
        await vm.RefreshAccountsAsync(CancellationToken.None);
        await vm.AddOfflineAccountCommand.ExecuteAsync("Alex_2");

        await vm.RemoveAccountCommand.ExecuteAsync(vm.ActiveAccount);

        Assert.DoesNotContain(await store.ListAsync(CancellationToken.None), a => a.Username == "Alex_2");
        Assert.True(vm.ActiveAccount!.IsOffline);
        Assert.Equal("Steve", vm.ActiveAccount.Username);
    }

    [Fact]
    public void IsValidOfflineName_FollowsMinecraftUsernameRules()
    {
        Assert.True(MainViewModel.IsValidOfflineName("Steve"));
        Assert.True(MainViewModel.IsValidOfflineName("The_LL"));
        Assert.False(MainViewModel.IsValidOfflineName(""));
        Assert.False(MainViewModel.IsValidOfflineName("a-b-c"));
    }

    private FileAccountStore NewStore() => new(Path.Combine(_dir, "accounts.v2.json"), Path.Combine(_dir, "accounts.json"));

    private static MainViewModel NewVm(IAccountStore store, out StubLauncherService service)
    {
        service = new StubLauncherService
        {
            VersionsToReturn = new[] { new VersionMetadata { Name = "26.3", Type = "release" } },
        };
        return new MainViewModel(service, new RecordingLogger(), microsoftAuth: null, accountStore: store);
    }
}
