using System.IO;
using CmlLib.Core;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// Unit tests for <see cref="CmlLibUnderlyingLauncher.BuildInstancePath"/>. An instance's game
/// directory holds its mods, saves and configs; the game jars, libraries, assets and Java
/// runtimes are installed once into the shared .minecraft and must be found there, or the
/// classpath points at files that don't exist and the JVM can't find the main class.
/// </summary>
public class BuildInstancePathTests
{
    private static readonly string Shared = Path.Combine(Path.GetTempPath(), "hyperion-shared-mc");
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "hyperion-instance-1");

    [Fact]
    public void GameDirectory_BecomesTheBasePath()
    {
        var path = CmlLibUnderlyingLauncher.BuildInstancePath(new MinecraftPath(Shared), GameDir);

        Assert.Equal(Path.GetFullPath(GameDir), Path.GetFullPath(path.BasePath));
    }

    [Fact]
    public void InstalledFiles_StayInTheSharedMinecraftFolder()
    {
        var shared = new MinecraftPath(Shared);

        var path = CmlLibUnderlyingLauncher.BuildInstancePath(shared, GameDir);

        Assert.Equal(shared.Versions, path.Versions);
        Assert.Equal(shared.Library, path.Library);
        Assert.Equal(shared.Assets, path.Assets);
        Assert.Equal(shared.Runtime, path.Runtime);
    }

    [Fact]
    public void LegacyResourcesFolder_LivesInTheGameDirectory()
    {
        // Pre-1.6 versions read virtual assets from <gameDir>/resources, so that one stays per instance.
        var path = CmlLibUnderlyingLauncher.BuildInstancePath(new MinecraftPath(Shared), GameDir);

        Assert.StartsWith(Path.GetFullPath(GameDir), Path.GetFullPath(path.Resource));
    }
}
