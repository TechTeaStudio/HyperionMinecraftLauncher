using TechTeaStudio.HyperionMinecraftLauncher.App.Cli;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Cli;

/// <summary>
/// Tests for the <see cref="CliEntryPoint"/> helpers that don't touch the launcher service
/// (full RunAsync end-to-end tests would require a CmlLib fake that's overkill for v0.28).
/// </summary>
public class CliEntryPointTests
{
    [Fact]
    public void ParseServerArg_Null_ReturnsNone()
    {
        var result = CliEntryPoint.ParseServerArg(null);
        Assert.IsType<QuickPlay.None>(result);
    }

    [Fact]
    public void ParseServerArg_Empty_ReturnsNone()
    {
        var result = CliEntryPoint.ParseServerArg(string.Empty);
        Assert.IsType<QuickPlay.None>(result);
    }

    [Fact]
    public void ParseServerArg_HostOnly_ReturnsMultiplayerWithDefaultPort()
    {
        var result = CliEntryPoint.ParseServerArg("mc.hypixel.net");
        var mp = Assert.IsType<QuickPlay.Multiplayer>(result);
        Assert.Equal("mc.hypixel.net", mp.Host);
        Assert.Equal(25565, mp.Port);
    }

    [Fact]
    public void ParseServerArg_HostAndPort_ReturnsMultiplayerWithParsedPort()
    {
        var result = CliEntryPoint.ParseServerArg("mc.hypixel.net:25566");
        var mp = Assert.IsType<QuickPlay.Multiplayer>(result);
        Assert.Equal("mc.hypixel.net", mp.Host);
        Assert.Equal(25566, mp.Port);
    }

    [Fact]
    public void ParseServerArg_HostWithGarbagePort_FallsBackToDefaultPort()
    {
        var result = CliEntryPoint.ParseServerArg("mc.hypixel.net:abc");
        var mp = Assert.IsType<QuickPlay.Multiplayer>(result);
        Assert.Equal("mc.hypixel.net:abc", mp.Host);
        Assert.Equal(25565, mp.Port);
    }

    [Fact]
    public void CliVersion_MatchesProjectVersion()
    {
        // Lockstep check: if someone bumps the .csproj <Version> they must also bump this
        // constant. Catches the "forgot to update --version output" regression.
        Assert.Equal("0.28.0", CliEntryPoint.CliVersion);
    }
}
