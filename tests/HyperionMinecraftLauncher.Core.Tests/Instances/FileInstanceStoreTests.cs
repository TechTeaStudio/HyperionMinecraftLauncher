using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances;

/// <summary>
/// Verifies the file-backed instance store round-trips an Instance, including the
/// IconKey property which the Edit Icon dialog mutates after creation.
/// </summary>
public class FileInstanceStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileInstanceStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "HMLTests_FileInstanceStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup - swallow lingering file-lock errors so the test process exits cleanly.
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsIconKey_LoadAllReturnsIt()
    {
        var store = new FileInstanceStore(_tempDir);
        var initial = new Instance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Edit icon target",
            VersionId = "1.21.5",
            IconKey = InstanceIcons.GrassBlock,
        };
        await store.SaveAsync(initial, CancellationToken.None);

        var loaded = await store.LoadAllAsync(CancellationToken.None);
        var sole = Assert.Single(loaded);
        Assert.Equal(InstanceIcons.GrassBlock, sole.IconKey);
    }

    [Fact]
    public async Task SaveAsync_OverwriteWithNewIconKey_RoundTripsThroughDisk()
    {
        // This is the test that covers the Edit Icon dialog's commit path: take an
        // existing on-disk instance, write it back with a different IconKey, and
        // assert the change survives a fresh LoadAllAsync (i.e. is persisted, not
        // just in-memory mutated).
        var store = new FileInstanceStore(_tempDir);
        var id = Guid.NewGuid().ToString("N");
        var original = new Instance
        {
            Id = id,
            Name = "Round-trip icon",
            VersionId = "1.21.5",
            IconKey = InstanceIcons.GrassBlock,
        };
        await store.SaveAsync(original, CancellationToken.None);

        var updated = original with { IconKey = InstanceIcons.DiamondPickaxe };
        await store.SaveAsync(updated, CancellationToken.None);

        // New store instance reading from the same directory - guarantees the value
        // came back off the file, not out of any cached state.
        var freshStore = new FileInstanceStore(_tempDir);
        var loaded = await freshStore.LoadAllAsync(CancellationToken.None);
        var match = loaded.Single(i => i.Id == id);
        Assert.Equal(InstanceIcons.DiamondPickaxe, match.IconKey);
        Assert.Equal("Round-trip icon", match.Name);
        Assert.Equal("1.21.5", match.VersionId);
    }
}
