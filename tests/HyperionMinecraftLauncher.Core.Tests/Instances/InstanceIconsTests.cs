using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances;

public class InstanceIconsTests
{
    [Theory]
    [InlineData(ModLoader.Fabric, InstanceIcons.Planks)]
    [InlineData(ModLoader.Quilt, InstanceIcons.Planks)]
    [InlineData(ModLoader.Forge, InstanceIcons.Cobblestone)]
    [InlineData(ModLoader.NeoForge, InstanceIcons.Cobblestone)]
    [InlineData(ModLoader.None, InstanceIcons.GrassBlock)]
    public void ForLoader_MapsEachLoaderToItsBlock(ModLoader loader, string expected)
    {
        Assert.Equal(expected, InstanceIcons.ForLoader(loader));
    }
}
