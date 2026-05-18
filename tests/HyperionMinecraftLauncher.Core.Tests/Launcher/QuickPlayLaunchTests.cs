using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// QuickPlay flows from the public service through the underlying launcher: the request
/// surface gains a <see cref="QuickPlay"/> property that defaults to <c>None</c>, and the
/// underlying receives the same request verbatim. The actual command-line emission lives
/// in <see cref="CmlLibUnderlyingLauncher"/> and is exercised by integration paths, not here.
/// </summary>
public class QuickPlayLaunchTests
{
    [Fact]
    public async Task LaunchAsync_QuickPlayNone_FlowsThroughUnchanged()
    {
        var fake = new FakeUnderlyingLauncher { ProcessIdToReturn = 1 };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var req = new LaunchRequest { VersionName = "1.21.5", Session = auth };
        var result = await service.LaunchAsync(req, null, CancellationToken.None);

        Assert.Equal(1, result.ProcessId);
        Assert.NotNull(fake.LastRequest);
        Assert.IsType<QuickPlay.None>(fake.LastRequest!.QuickPlay);
    }

    [Fact]
    public async Task LaunchAsync_QuickPlaySingleplayer_ObservedAtUnderlying()
    {
        var fake = new FakeUnderlyingLauncher { ProcessIdToReturn = 2 };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var req = new LaunchRequest
        {
            VersionName = "1.21.5",
            Session = auth,
            QuickPlay = new QuickPlay.Singleplayer("My Cool World"),
        };
        var result = await service.LaunchAsync(req, null, CancellationToken.None);

        Assert.Equal(2, result.ProcessId);
        var sp = Assert.IsType<QuickPlay.Singleplayer>(fake.LastRequest!.QuickPlay);
        Assert.Equal("My Cool World", sp.WorldFolderName);
    }

    [Fact]
    public async Task LaunchAsync_QuickPlayMultiplayer_ObservedAtUnderlying()
    {
        var fake = new FakeUnderlyingLauncher { ProcessIdToReturn = 3 };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var req = new LaunchRequest
        {
            VersionName = "1.21.5",
            Session = auth,
            QuickPlay = new QuickPlay.Multiplayer("mc.hypixel.net", 25565),
        };
        var result = await service.LaunchAsync(req, null, CancellationToken.None);

        Assert.Equal(3, result.ProcessId);
        var mp = Assert.IsType<QuickPlay.Multiplayer>(fake.LastRequest!.QuickPlay);
        Assert.Equal("mc.hypixel.net", mp.Host);
        Assert.Equal(25565, mp.Port);
    }

    [Fact]
    public void LaunchRequest_DefaultsToQuickPlayNone()
    {
        var req = new LaunchRequest
        {
            VersionName = "1.21.5",
            Session = new AuthResult { Username = "Steve", Uuid = "u", AccessToken = "t", IsOffline = true },
        };
        Assert.IsType<QuickPlay.None>(req.QuickPlay);
    }
}
