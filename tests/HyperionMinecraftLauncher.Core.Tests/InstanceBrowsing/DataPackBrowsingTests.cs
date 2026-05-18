using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>
/// Covers the per-world <c>datapacks/</c> scan: two synthetic worlds, each with its
/// own packs. The browser must flatten into one list but tag every entry with the
/// owning <c>WorldFolderName</c> so the UI can group rows.
/// </summary>
public class DataPackBrowsingTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public DataPackBrowsingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-datapacks-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "T", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListDataPacksAsync_GroupsAcrossWorlds_AndReadsEnabledFlag()
    {
        // saves/
        //   survival/datapacks/pack-a.zip
        //   survival/datapacks/pack-b.zip.disabled
        //   creative/datapacks/pack-c.zip
        //   no-packs/level.dat            (world exists but no datapacks folder)
        var saves = Path.Combine(_root, "saves");
        Directory.CreateDirectory(saves);

        var survival = Path.Combine(saves, "survival", "datapacks");
        Directory.CreateDirectory(survival);
        File.WriteAllText(Path.Combine(survival, "pack-a.zip"), "a");
        File.WriteAllText(Path.Combine(survival, "pack-b.zip.disabled"), "bb");

        var creative = Path.Combine(saves, "creative", "datapacks");
        Directory.CreateDirectory(creative);
        File.WriteAllText(Path.Combine(creative, "pack-c.zip"), "ccc");

        Directory.CreateDirectory(Path.Combine(saves, "no-packs"));
        File.WriteAllText(Path.Combine(saves, "no-packs", "level.dat"), "");

        var result = await new FileSystemInstanceBrowser().ListDataPacksAsync(_instance, CancellationToken.None);

        Assert.Equal(3, result.Count);

        var survivalPacks = result.Where(e => e.WorldFolderName == "survival").ToList();
        Assert.Equal(2, survivalPacks.Count);
        Assert.True(survivalPacks.Single(p => p.Filename == "pack-a.zip").IsEnabled);
        Assert.False(survivalPacks.Single(p => p.Filename == "pack-b.zip.disabled").IsEnabled);

        var creativePacks = result.Where(e => e.WorldFolderName == "creative").ToList();
        Assert.Single(creativePacks);
        Assert.Equal("pack-c.zip", creativePacks[0].Filename);
        Assert.True(creativePacks[0].IsEnabled);

        // World with no datapacks/ subdirectory must not surface anything.
        Assert.DoesNotContain(result, e => e.WorldFolderName == "no-packs");
    }

    [Fact]
    public async Task ListDataPacksAsync_NoSavesDirectory_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListDataPacksAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListDataPacksAsync_IgnoresFilesThatAreNotZipsOrDisabled()
    {
        var pack = Path.Combine(_root, "saves", "world1", "datapacks");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "real.zip"), "x");
        File.WriteAllText(Path.Combine(pack, "leftover.txt"), "ignore");

        var result = await new FileSystemInstanceBrowser().ListDataPacksAsync(_instance, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("real.zip", result[0].Filename);
        Assert.Equal("world1", result[0].WorldFolderName);
    }
}
