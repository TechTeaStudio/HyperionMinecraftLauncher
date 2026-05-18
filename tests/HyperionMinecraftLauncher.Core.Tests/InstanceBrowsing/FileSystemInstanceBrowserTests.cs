using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using fNbt;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>
/// Drives the file-system browser through a synthetic per-instance gameDir:
/// <list type="bullet">
///   <item><c>screenshots/</c> with three PNGs at known mtimes, expect newest-first ordering and decoded W/H.</item>
///   <item><c>saves/&lt;name&gt;/level.dat</c> compressed compounds with LevelName + LastPlayed + Version.</item>
///   <item><c>servers.dat</c> with two server entries.</item>
/// </list>
/// </summary>
public class FileSystemInstanceBrowserTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public FileSystemInstanceBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-browser-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "Test", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListScreenshotsAsync_OrdersByMtimeDescending_AndReadsIhdrSize()
    {
        var dir = Path.Combine(_root, "screenshots");
        Directory.CreateDirectory(dir);
        var oldFile = Path.Combine(dir, "old.png");
        var midFile = Path.Combine(dir, "mid.png");
        var newFile = Path.Combine(dir, "new.png");
        WriteFakePng(oldFile, 320, 240);
        WriteFakePng(midFile, 640, 480);
        WriteFakePng(newFile, 1920, 1080);

        var origin = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(oldFile, origin);
        File.SetLastWriteTimeUtc(midFile, origin.AddHours(1));
        File.SetLastWriteTimeUtc(newFile, origin.AddHours(2));

        // A non-PNG should be ignored.
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignored");

        var result = await new FileSystemInstanceBrowser().ListScreenshotsAsync(_instance, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("new.png", result[0].Filename);
        Assert.Equal("mid.png", result[1].Filename);
        Assert.Equal("old.png", result[2].Filename);
        Assert.Equal(1920, result[0].Width);
        Assert.Equal(1080, result[0].Height);
        Assert.Equal(640, result[1].Width);
        Assert.Equal(480, result[1].Height);
        Assert.True(result[0].SizeBytes > 0);
    }

    [Fact]
    public async Task ListScreenshotsAsync_MissingDirectory_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListScreenshotsAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListWorldsAsync_ReturnsOnlyFoldersWithLevelDat_AndParsesDisplayName()
    {
        var savesDir = Path.Combine(_root, "saves");
        Directory.CreateDirectory(savesDir);

        // World with full level.dat.
        var w1 = Path.Combine(savesDir, "my-survival");
        Directory.CreateDirectory(w1);
        WriteLevelDat(Path.Combine(w1, "level.dat"), "Survival Run", lastPlayedMs: 1_700_000_000_000L, version: "1.21.5", gameType: 0);

        // World without level.dat - must be skipped.
        Directory.CreateDirectory(Path.Combine(savesDir, "incomplete"));

        // Stray file in saves/ - must be skipped.
        File.WriteAllText(Path.Combine(savesDir, "notes.txt"), "no");

        var result = await new FileSystemInstanceBrowser().ListWorldsAsync(_instance, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("my-survival", result[0].FolderName);
        Assert.Equal("Survival Run", result[0].DisplayName);
        Assert.Equal("1.21.5", result[0].Version);
        Assert.Equal(0, result[0].GameMode);
        Assert.NotNull(result[0].LastPlayedIso);
    }

    [Fact]
    public async Task ListWorldsAsync_NoSavesDirectory_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListWorldsAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListServersAsync_ReadsServersDat_ScopedToGameDir()
    {
        var path = Path.Combine(_root, "servers.dat");
        WriteServersFile(path, ("Hypixel", "mc.hypixel.net"), ("Local", "localhost:25565"));

        var result = await new FileSystemInstanceBrowser().ListServersAsync(_instance, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Hypixel", result[0].Name);
        Assert.Equal("Local", result[1].Name);
    }

    [Fact]
    public async Task ListServersAsync_NoServersDat_ReturnsEmpty()
    {
        var result = await new FileSystemInstanceBrowser().ListServersAsync(_instance, CancellationToken.None);
        Assert.Empty(result);
    }

    // ----------------- helpers -----------------

    /// <summary>
    /// Write the bare minimum a PNG-IHDR parser needs: the 8-byte signature, then a 13-byte
    /// IHDR chunk (length 13, "IHDR", width, height, bit depth, color type, compression,
    /// filter, interlace), followed by a trivial IDAT and IEND. The renderer never decodes it.
    /// </summary>
    private static void WriteFakePng(string path, int width, int height)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new byte[8 + 13]; // chunk length (4) + type "IHDR" (4) + data (13)
        WriteBigEndianInt(ihdr, 0, 13);
        ihdr[4] = (byte)'I'; ihdr[5] = (byte)'H'; ihdr[6] = (byte)'D'; ihdr[7] = (byte)'R';
        WriteBigEndianInt(ihdr, 8, width);
        WriteBigEndianInt(ihdr, 12, height);
        ihdr[16] = 8;  // bit depth
        ihdr[17] = 2;  // color type RGB
        ihdr[18] = 0;  // compression
        ihdr[19] = 0;  // filter
        ihdr[20] = 0;  // interlace

        using var fs = File.Create(path);
        fs.Write(sig, 0, sig.Length);
        fs.Write(ihdr, 0, ihdr.Length);
        // CRC of IHDR data + type - we skip it. Parser only needs the IHDR header, not the CRC.
        fs.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);
        // No IDAT/IEND - the parser stops after IHDR, and the renderer is not invoked.
    }

    private static void WriteBigEndianInt(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    /// <summary>Write a gzip-compressed Minecraft <c>level.dat</c> NBT (root compound containing <c>Data</c>).</summary>
    internal static void WriteLevelDat(string path, string levelName, long lastPlayedMs, string version, int gameType)
    {
        var data = new NbtCompound("Data")
        {
            new NbtString("LevelName", levelName),
            new NbtLong("LastPlayed", lastPlayedMs),
            new NbtInt("GameType", gameType),
            new NbtCompound("Version")
            {
                new NbtString("Name", version),
                new NbtInt("Id", 3700),
            },
        };
        var root = new NbtCompound("") { data };
        var file = new NbtFile(root);
        file.SaveToFile(path, NbtCompression.GZip);
    }

    private static void WriteServersFile(string path, params (string name, string ip)[] entries)
    {
        var list = new NbtList("servers", NbtTagType.Compound);
        foreach (var (name, ip) in entries)
        {
            list.Add(new NbtCompound
            {
                new NbtString("name", name),
                new NbtString("ip", ip),
            });
        }
        var root = new NbtCompound("") { list };
        var file = new NbtFile(root);
        file.SaveToFile(path, NbtCompression.None);
    }
}
