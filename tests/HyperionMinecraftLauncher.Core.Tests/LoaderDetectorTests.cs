using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class LoaderDetectorTests
{
    [Theory]
    [InlineData("1.21.5", "net.minecraft.client.main.Main", null, ModLoader.None)]
    [InlineData("1.20.1-forge-47.4.5", "cpw.mods.bootstraplauncher.BootstrapLauncher", "1.20.1", ModLoader.Forge)]
    [InlineData("1.18.2-forge-40.2.0", "cpw.mods.modlauncher.Launcher", "1.18.2", ModLoader.Forge)]
    [InlineData("1.20.4-neoforge-20.4.234", "cpw.mods.bootstraplauncher.BootstrapLauncher", "1.20.4", ModLoader.NeoForge)]
    [InlineData("fabric-loader-0.15.11-1.20.4", "net.fabricmc.loader.impl.launch.knot.KnotClient", "1.20.4", ModLoader.Fabric)]
    [InlineData("quilt-loader-0.26.0-1.20.4", "org.quiltmc.loader.impl.launch.knot.KnotClient", "1.20.4", ModLoader.Quilt)]
    [InlineData("1.7.10", "net.minecraft.launchwrapper.Launch", null, ModLoader.None)]  // bare launchwrapper without "optifine" is vanilla/legacy
    [InlineData("1.20.4-OptiFine_HD_U_I7_pre6", "net.minecraft.launchwrapper.Launch", "1.20.4", ModLoader.OptiFine)]
    [InlineData("1.7.10-Forge10.13.4", "cpw.mods.fml.common.launcher.FMLTweaker", "1.7.10", ModLoader.LegacyForge)]
    [InlineData("strange-mystery-pack", "", "1.20.1", ModLoader.Other)] // inheritsFrom set, mainClass empty
    public void Detect_ProducesExpectedLoader(string id, string mainClass, string? inheritsFrom, ModLoader expected)
    {
        var actual = LoaderDetector.Detect(id, mainClass, inheritsFrom);
        Assert.Equal(expected, actual);
    }
}
