using System;
using System.Net.Http;
using CmlLib.Core;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.App;

/// <summary>
/// The pieces a launch needs besides the CmlLib launcher itself: the JRE manager (Adoptium plus
/// the system probe) and the mod-loader installer. Shared by the window (App.axaml.cs) and CLI
/// mode (Program.cs) so both launch an instance the same way.
/// </summary>
internal static class LaunchPipeline
{
    /// <summary>
    /// JRE manager: managed cache first, then an existing JDK / JRE under JAVA_HOME, PATH or a
    /// well-known vendor directory, then a Temurin download from Adoptium.
    /// </summary>
    public static IJavaRuntimeManager CreateJavaRuntimeManager()
    {
        // The JRE download is the big one (~50-180 MB), hence the long timeout.
        var javaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        return new AdoptiumJavaRuntimeManager(
            javaHttp,
            AdoptiumJavaRuntimeManager.DefaultRootDirectory(),
            AdoptiumJavaRuntimeManager.DetectOs(),
            AdoptiumJavaRuntimeManager.DetectArch(),
            new SystemJavaProbe());
    }

    /// <summary>Forge / NeoForge / Fabric / Quilt installer writing into <paramref name="launcher"/>'s .minecraft.</summary>
    public static IModLoaderInstaller CreateModLoaderInstaller(HttpClient http, MinecraftLauncher launcher) =>
        new CmlLibModLoaderInstaller(
            forge: new CmlLibForgeUnderlying(launcher),
            neoForge: new CmlLibNeoForgeUnderlying(launcher),
            fabric: new CmlLibFabricUnderlying(http, launcher),
            quilt: new CmlLibQuiltUnderlying(http, launcher));
}
