using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>
/// Mirrors <see cref="ResourcePackBrowsingTests"/> for <c>&lt;gameDir&gt;/shaderpacks/</c>:
/// Iris / OptiFine share the same <c>.zip.disabled</c> convention.
/// </summary>
public class ShaderPackBrowsingTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public ShaderPackBrowsingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-shaderpacks-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "T", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListShaderPacksAsync_ListsZipsAndDisabled_IgnoresOtherFiles()
    {
        var dir = Path.Combine(_root, "shaderpacks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "BSL.zip"), "bsl");
        File.WriteAllText(Path.Combine(dir, "ComplementaryReimagined.zip.disabled"), "creo");
        File.WriteAllText(Path.Combine(dir, "shadersmod.json"), "ignore");

        var result = await new FileSystemInstanceBrowser().ListShaderPacksAsync(_instance, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(e => e.Filename == "BSL.zip").IsEnabled);
        Assert.False(result.Single(e => e.Filename == "ComplementaryReimagined.zip.disabled").IsEnabled);
    }

    [Fact]
    public async Task ListShaderPacksAsync_MissingDirectory_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListShaderPacksAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ToggleShaderPack_RoundTripsZipDisabledSuffix()
    {
        var dir = Path.Combine(_root, "shaderpacks");
        Directory.CreateDirectory(dir);
        var enabledPath = Path.Combine(dir, "Sildur.zip");
        File.WriteAllText(enabledPath, "x");

        var disabledPath = enabledPath + ".disabled";
        File.Move(enabledPath, disabledPath);
        var afterDisable = await new FileSystemInstanceBrowser().ListShaderPacksAsync(_instance, CancellationToken.None);
        Assert.False(afterDisable.Single().IsEnabled);

        File.Move(disabledPath, enabledPath);
        var afterEnable = await new FileSystemInstanceBrowser().ListShaderPacksAsync(_instance, CancellationToken.None);
        Assert.True(afterEnable.Single().IsEnabled);
    }
}
