using System.IO;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Import.MultiMc;

/// <summary>
/// Verify <see cref="MultiMcPackParser"/> picks up each supported component UID, falls back
/// to vanilla when no loader UID is present, and produces friendly <see cref="InvalidDataException"/>
/// errors for malformed JSON.
/// </summary>
public class MultiMcPackParserTests
{
    [Fact]
    public void Parse_VanillaPack_ReturnsLoaderNone()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.21.4" }
              ],
              "formatVersion": 1
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal("1.21.4", pack.MinecraftVersion);
        Assert.Equal(ModLoader.None, pack.Loader);
        Assert.Null(pack.LoaderVersion);
    }

    [Fact]
    public void Parse_FabricPack_ReturnsFabricWithLoaderVersion()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.fabricmc.intermediary", "version": "1.21.4" },
                { "uid": "net.fabricmc.fabric-loader", "version": "0.16.10" },
                { "uid": "net.minecraft", "version": "1.21.4" }
              ]
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal("1.21.4", pack.MinecraftVersion);
        Assert.Equal(ModLoader.Fabric, pack.Loader);
        Assert.Equal("0.16.10", pack.LoaderVersion);
    }

    [Fact]
    public void Parse_ForgePack_ReturnsForge()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.20.1" },
                { "uid": "net.minecraftforge", "version": "47.4.5" }
              ]
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal(ModLoader.Forge, pack.Loader);
        Assert.Equal("47.4.5", pack.LoaderVersion);
    }

    [Fact]
    public void Parse_NeoForgePack_ReturnsNeoForge()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.21.4" },
                { "uid": "net.neoforged", "version": "21.4.10-beta" }
              ]
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal(ModLoader.NeoForge, pack.Loader);
        Assert.Equal("21.4.10-beta", pack.LoaderVersion);
    }

    [Fact]
    public void Parse_QuiltPack_ReturnsQuilt()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.21.0" },
                { "uid": "org.quiltmc.quilt-loader", "version": "0.26.0" }
              ]
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal(ModLoader.Quilt, pack.Loader);
        Assert.Equal("0.26.0", pack.LoaderVersion);
    }

    [Fact]
    public void Parse_UnknownLoaderUid_FallsBackToVanilla()
    {
        var raw = """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.7.10" },
                { "uid": "com.example.someweirdloader", "version": "1.0" }
              ]
            }
            """;

        var pack = MultiMcPackParser.Parse(raw);

        Assert.Equal("1.7.10", pack.MinecraftVersion);
        Assert.Equal(ModLoader.None, pack.Loader);
        Assert.Null(pack.LoaderVersion);
    }

    [Fact]
    public void Parse_MissingMinecraftComponent_Throws()
    {
        var raw = """{ "components": [ { "uid": "net.fabricmc.fabric-loader", "version": "0.16.0" } ] }""";

        Assert.Throws<InvalidDataException>(() => MultiMcPackParser.Parse(raw));
    }

    [Fact]
    public void Parse_MissingComponentsArray_Throws()
    {
        var raw = """{ "formatVersion": 1 }""";

        Assert.Throws<InvalidDataException>(() => MultiMcPackParser.Parse(raw));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<InvalidDataException>(() => MultiMcPackParser.Parse("{ this is not json"));
    }
}
