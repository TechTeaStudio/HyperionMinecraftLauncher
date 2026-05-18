using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// Unit tests for <see cref="CmlLibUnderlyingLauncher.BuildQuickPlayArgs"/>. Pure function -
/// no CmlLib process build needed. The Minecraft 1.20+ launch args are documented at
/// https://minecraft.wiki/w/Minecraft_launch_options#Quick_Play.
/// </summary>
public class BuildQuickPlayArgsTests
{
    [Fact]
    public void None_ReturnsEmpty()
    {
        var args = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.None());
        Assert.Empty(args);
    }

    [Fact]
    public void Singleplayer_EmitsQuickPlaySingleplayerFlagAndWorldName()
    {
        var args = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.Singleplayer("My World"));
        Assert.Equal(new[] { "--quickPlaySingleplayer", "My World" }, args);
    }

    [Fact]
    public void Multiplayer_EmitsQuickPlayMultiplayerFlagAndHostColonPort()
    {
        var args = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.Multiplayer("mc.hypixel.net", 25565));
        Assert.Equal(new[] { "--quickPlayMultiplayer", "mc.hypixel.net:25565" }, args);
    }

    [Fact]
    public void Multiplayer_DefaultsPortTo25565()
    {
        var args = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.Multiplayer("play.example.com"));
        Assert.Equal(new[] { "--quickPlayMultiplayer", "play.example.com:25565" }, args);
    }

    [Fact]
    public void Multiplayer_CustomPort_AppearsInHostColonPort()
    {
        var args = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.Multiplayer("example.com", 19132));
        Assert.Equal(new[] { "--quickPlayMultiplayer", "example.com:19132" }, args);
    }
}
