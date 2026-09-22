using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>
/// Covers <see cref="FileSystemInstanceBrowser.ListResourcePacksAsync"/>: synthetic
/// <c>&lt;gameDir&gt;/resourcepacks/</c> with a mix of <c>.zip</c> + <c>.zip.disabled</c>
/// + stray files. Toggle is verified via the rename round-trip that the UI invokes.
/// </summary>
public class ResourcePackBrowsingTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public ResourcePackBrowsingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-respacks-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "T", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListResourcePacksAsync_ListsZipsAndDisabled_IgnoresOtherFiles()
    {
        var dir = Path.Combine(_root, "resourcepacks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Alpha.zip"), "alphazip");
        File.WriteAllText(Path.Combine(dir, "Bravo.zip.disabled"), "bravodisabled");
        File.WriteAllText(Path.Combine(dir, "README.txt"), "ignore me");

        var result = await new FileSystemInstanceBrowser().ListResourcePacksAsync(_instance, CancellationToken.None);

        Assert.Equal(2, result.Count);
        var alpha = result.Single(e => e.Filename == "Alpha.zip");
        Assert.True(alpha.IsEnabled);
        Assert.Equal(8, alpha.SizeBytes);
        var bravo = result.Single(e => e.Filename == "Bravo.zip.disabled");
        Assert.False(bravo.IsEnabled);
    }

    [Fact]
    public async Task ListResourcePacksAsync_CarriesIconDescriptionAndDisplayName()
    {
        var dir = Path.Combine(_root, "resourcepacks");
        Directory.CreateDirectory(dir);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 9 };
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "Fresh Animations.zip.disabled"), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("pack.png").Open()) s.Write(png);
            using (var w = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open()))
                w.Write("{\"pack\":{\"pack_format\":15,\"description\":\"§4By FreshLX\"}}");
        }

        var pack = Assert.Single(await new FileSystemInstanceBrowser().ListResourcePacksAsync(_instance, CancellationToken.None));

        Assert.Equal("Fresh Animations", pack.DisplayName);
        Assert.Equal(png, pack.IconPng);
        Assert.Equal(("By FreshLX", "#AA0000"), (pack.Description.Single().Text, pack.Description.Single().Color));
    }

    [Fact]
    public async Task ListShaderPacksAsync_CarriesTheIconWhenThePackHasOne()
    {
        var dir = Path.Combine(_root, "shaderpacks");
        Directory.CreateDirectory(dir);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 7 };
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "BSL_v8.4.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("shaders/logo.png").Open()) s.Write(png);
        }

        var pack = Assert.Single(await new FileSystemInstanceBrowser().ListShaderPacksAsync(_instance, CancellationToken.None));

        Assert.Equal("BSL_v8.4", pack.DisplayName);
        Assert.Equal(png, pack.IconPng);
    }

    [Fact]
    public async Task ListResourcePacksAsync_MissingDirectory_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListResourcePacksAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ToggleResourcePack_RoundTripsZipDisabledSuffix()
    {
        var dir = Path.Combine(_root, "resourcepacks");
        Directory.CreateDirectory(dir);
        var enabledPath = Path.Combine(dir, "Faithful.zip");
        File.WriteAllText(enabledPath, "x");

        // Toggle off -> .zip.disabled
        var disabledPath = enabledPath + ".disabled";
        File.Move(enabledPath, disabledPath);
        Assert.False(File.Exists(enabledPath));
        Assert.True(File.Exists(disabledPath));
        var afterDisable = await new FileSystemInstanceBrowser().ListResourcePacksAsync(_instance, CancellationToken.None);
        Assert.False(afterDisable.Single().IsEnabled);

        // Toggle back on -> .zip
        File.Move(disabledPath, enabledPath);
        var afterEnable = await new FileSystemInstanceBrowser().ListResourcePacksAsync(_instance, CancellationToken.None);
        Assert.True(afterEnable.Single().IsEnabled);
    }
}
