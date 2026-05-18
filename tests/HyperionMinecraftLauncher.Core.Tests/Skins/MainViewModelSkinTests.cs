using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

public class MainViewModelSkinTests : IDisposable
{
    private readonly string _historyRoot;

    public MainViewModelSkinTests()
    {
        _historyRoot = Path.Combine(Path.GetTempPath(), "hypmcl-vm-skintests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_historyRoot)) Directory.Delete(_historyRoot, recursive: true); }
        catch { }
    }

    [Fact]
    public void UploadSkin_CanExecute_FalseWhenSignedOut()
    {
        var skinService = new FakeSkinService();
        var historyStore = new FileSkinHistoryStore(_historyRoot);
        var vm = NewVm(skinService, historyStore);

        Assert.False(vm.UploadSkinCommand.CanExecute(null));
    }

    [Fact]
    public async Task TryRefreshProfile_PopulatesOwnedSkinsAndCapes()
    {
        var skinService = new FakeSkinService
        {
            ProfileToReturn = new PlayerProfile
            {
                Id = "id1",
                Name = "Notch",
                Skins = new[]
                {
                    new OwnedSkin { Id = "s1", State = "ACTIVE", Url = "u", Variant = "CLASSIC" },
                },
                Capes = new[]
                {
                    new OwnedCape { Id = "c1", State = "INACTIVE", Url = "u", Alias = "A" },
                    new OwnedCape { Id = "c2", State = "ACTIVE",   Url = "u", Alias = "B" },
                },
            },
        };
        var vm = NewVm(skinService, new FileSkinHistoryStore(_historyRoot));

        await vm.TryRefreshProfileAsync("token-abc");

        Assert.Equal("token-abc", skinService.LastTokenSeen);
        Assert.Single(vm.OwnedSkins);
        Assert.Equal(2, vm.OwnedCapes.Count);
        Assert.True(vm.HasOwnedCapes);
        Assert.NotNull(vm.SelectedActiveCape);
        Assert.Equal("c2", vm.SelectedActiveCape!.Id);
    }

    [Fact]
    public async Task ReloadHistory_PopulatesObservableCollection()
    {
        var historyStore = new FileSkinHistoryStore(_historyRoot);
        await historyStore.AppendAsync(MakePng(), SkinVariant.Slim, CancellationToken.None);
        await historyStore.AppendAsync(MakePng(), SkinVariant.Classic, CancellationToken.None);

        var vm = NewVm(new FakeSkinService(), historyStore);
        await vm.ReloadHistoryAsync();

        Assert.Equal(2, vm.SkinHistory.Count);
    }

    [Fact]
    public void OwnedCapes_Empty_HasOwnedCapesFalse()
    {
        var vm = NewVm(new FakeSkinService(), new FileSkinHistoryStore(_historyRoot));
        Assert.False(vm.HasOwnedCapes);
    }

    private static byte[] MakePng()
    {
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02 };
    }

    private static MainViewModel NewVm(ISkinService skinService, ISkinHistoryStore historyStore)
    {
        var launcher = new SkinTestsStubLauncher();
        return new MainViewModel(launcher, new SkinTestsLogger(),
            microsoftAuth: null,
            settingsStore: null,
            skinService: skinService,
            skinHistory: historyStore);
    }

    private sealed class FakeSkinService : ISkinService
    {
        public PlayerProfile ProfileToReturn { get; set; } = new()
        {
            Id = "id",
            Name = "name",
            Skins = Array.Empty<OwnedSkin>(),
            Capes = Array.Empty<OwnedCape>(),
        };
        public string? LastTokenSeen { get; private set; }
        public byte[]? LastUploadedBytes { get; private set; }
        public SkinVariant? LastUploadedVariant { get; private set; }
        public string? LastSetCapeId { get; private set; }
        public bool ClearCapeCalled { get; private set; }

        public Task UploadSkinAsync(string accessToken, byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken)
        {
            LastTokenSeen = accessToken;
            LastUploadedBytes = pngBytes;
            LastUploadedVariant = variant;
            return Task.CompletedTask;
        }

        public Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
        {
            LastTokenSeen = accessToken;
            return Task.FromResult(ProfileToReturn);
        }

        public Task SetActiveCapeAsync(string accessToken, string capeId, CancellationToken cancellationToken)
        {
            LastTokenSeen = accessToken;
            LastSetCapeId = capeId;
            return Task.CompletedTask;
        }

        public Task ClearActiveCapeAsync(string accessToken, CancellationToken cancellationToken)
        {
            LastTokenSeen = accessToken;
            ClearCapeCalled = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>Minimal stub launcher service for these VM-only tests.</summary>
    private sealed class SkinTestsStubLauncher : IMinecraftLauncherService
    {
        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Versions.VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Versions.VersionMetadata>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Versions.VersionMetadata>());

        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.InstalledVersion>());

        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles.LauncherProfile>());

        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry>> ListServersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry>());

        public Task<IReadOnlyDictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?>> PingServersAsync(IEnumerable<TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.ServerListEntry> entries, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?>>(new Dictionary<string, TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping.ServerStatus?>());

        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry>> ListNewsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.News.NewsEntry>());

        public Task<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance>> ListInstancesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance>>(Array.Empty<TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance>());

        public Task SaveInstanceAsync(TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance instance, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteInstanceAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new AuthResult { Username = "x", Uuid = "x", AccessToken = "x", IsOffline = true });

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
            => Task.FromResult(new LaunchResult { ProcessId = 1, VersionName = "x" });
    }

    private sealed class SkinTestsLogger : ILauncherLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
