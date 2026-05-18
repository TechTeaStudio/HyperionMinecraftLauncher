using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Backups;

/// <summary>
/// Synthetic gameDir with two worlds (world1, world2) plus an empty saves/empty1 folder
/// that the service must skip. Drives BackupAllWorldsAsync / ListBackupsAsync /
/// PruneAsync / RestoreAsync end-to-end against the real file system.
/// </summary>
public class FileSystemBackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public FileSystemBackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-backups-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "Test", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task BackupAllWorldsAsync_ZipsEachNonEmptyWorld_AndSkipsEmpty()
    {
        // saves/world1/level.dat + saves/world2/level.dat + saves/emptyworld/
        SeedWorld("world1");
        SeedWorld("world2");
        Directory.CreateDirectory(Path.Combine(_root, "saves", "emptyworld"));

        var svc = new FileSystemBackupService();
        var entries = await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        var sourceWorlds = entries.Select(e => e.SourceWorldName).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "world1", "world2" }, sourceWorlds);

        // The actual zip files exist under <gameDir>/backups/ and contain a level.dat.
        var backupsDir = Path.Combine(_root, "backups");
        Assert.True(Directory.Exists(backupsDir));
        var zips = Directory.GetFiles(backupsDir, "*.zip");
        Assert.Equal(2, zips.Length);
        foreach (var z in zips)
        {
            using var fs = File.OpenRead(z);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            // CreateFromDirectory with includeBaseDirectory=true wraps entries under "{world}/...".
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("level.dat"));
        }
    }

    [Fact]
    public async Task ListBackupsAsync_ReturnsNewestFirst()
    {
        SeedWorld("world1");

        // Two backups for the same world, fake clock so timestamps are deterministic.
        var t0 = new DateTimeOffset(2026, 5, 18, 9, 30, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 5, 18, 10, 30, 0, TimeSpan.Zero);

        var clock = new MutableClock(t0);
        var svc = new FileSystemBackupService(() => clock.Now);
        await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);
        clock.Now = t1;
        await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);

        var listed = await svc.ListBackupsAsync(_instance, CancellationToken.None);
        Assert.Equal(2, listed.Count);
        Assert.True(listed[0].CreatedAt > listed[1].CreatedAt);
        Assert.All(listed, e => Assert.Equal("world1", e.SourceWorldName));
        Assert.All(listed, e => Assert.True(e.SizeBytes > 0));
    }

    [Fact]
    public async Task PruneAsync_KeepLatest1_DeletesOlderBackupsPerWorld()
    {
        SeedWorld("world1");
        SeedWorld("world2");

        var t0 = new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);

        var clock = new MutableClock(t0);
        var svc = new FileSystemBackupService(() => clock.Now);
        await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);
        clock.Now = t1;
        await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);

        // Both worlds have 2 backups each (4 total).
        var before = await svc.ListBackupsAsync(_instance, CancellationToken.None);
        Assert.Equal(4, before.Count);

        await svc.PruneAsync(_instance, keepLatest: 1, CancellationToken.None);

        var after = await svc.ListBackupsAsync(_instance, CancellationToken.None);
        Assert.Equal(2, after.Count);
        // The remaining one per world should be the newer (t1) backup.
        foreach (var e in after)
            Assert.Equal(t1, e.CreatedAt);
    }

    [Fact]
    public async Task PruneAsync_KeepLatestZero_DoesNotDelete()
    {
        SeedWorld("world1");
        var svc = new FileSystemBackupService();
        await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);

        await svc.PruneAsync(_instance, keepLatest: 0, CancellationToken.None);

        var remaining = await svc.ListBackupsAsync(_instance, CancellationToken.None);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task RestoreAsync_ExtractsIntoRestoredFolder_LeavesOriginalIntact()
    {
        SeedWorld("world1");

        var svc = new FileSystemBackupService();
        var entries = await svc.BackupAllWorldsAsync(_instance, CancellationToken.None);
        var entry = Assert.Single(entries);

        await svc.RestoreAsync(entry, CancellationToken.None);

        var savesDir = Path.Combine(_root, "saves");
        Assert.True(Directory.Exists(Path.Combine(savesDir, "world1")));
        var restored = Directory.GetDirectories(savesDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && n.StartsWith("world1-restored-"))
            .ToArray();
        Assert.Single(restored);
        var restoredRoot = Path.Combine(savesDir, restored[0]!);
        // CreateFromDirectory(..., includeBaseDirectory: true) places contents under {worldName}/.
        Assert.True(File.Exists(Path.Combine(restoredRoot, "world1", "level.dat")));
    }

    [Fact]
    public async Task BackupWorldAsync_OneWorldOnly_DoesNotTouchOthers()
    {
        SeedWorld("world1");
        SeedWorld("world2");

        var svc = new FileSystemBackupService();
        var entry = await svc.BackupWorldAsync(_instance, "world1", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("world1", entry!.SourceWorldName);

        var backupsDir = Path.Combine(_root, "backups");
        var zips = Directory.GetFiles(backupsDir, "*.zip");
        Assert.Single(zips);
    }

    [Fact]
    public async Task ListBackupsAsync_MissingBackupsDir_ReturnsEmpty()
    {
        // No saves dir, no backups dir.
        var svc = new FileSystemBackupService();
        var listed = await svc.ListBackupsAsync(_instance, CancellationToken.None);
        Assert.Empty(listed);
    }

    private void SeedWorld(string worldName)
    {
        var worldDir = Path.Combine(_root, "saves", worldName);
        Directory.CreateDirectory(worldDir);
        File.WriteAllBytes(Path.Combine(worldDir, "level.dat"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    }

    private sealed class MutableClock
    {
        public DateTimeOffset Now { get; set; }
        public MutableClock(DateTimeOffset initial) { Now = initial; }
    }
}
