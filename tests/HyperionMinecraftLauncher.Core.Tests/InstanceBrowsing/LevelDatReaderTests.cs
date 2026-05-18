using System;
using System.Globalization;
using System.IO;
using fNbt;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

/// <summary>
/// Drives <see cref="LevelDatReader"/> with synthetic gzip-compressed level.dat NBT files.
/// </summary>
public class LevelDatReaderTests : IDisposable
{
    private readonly string _temp;

    public LevelDatReaderTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-leveldat-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public void Read_FullLevelDat_ExtractsDisplayName_LastPlayedIso_AndVersion()
    {
        var path = Path.Combine(_temp, "level.dat");
        // Tuesday 14 November 2023 22:13:20 UTC = 1_700_000_000_000 ms.
        const long lastPlayedMs = 1_700_000_000_000L;
        FileSystemInstanceBrowserTests.WriteLevelDat(path, "My Survival World", lastPlayedMs, "1.21.5", gameType: 0);

        var info = LevelDatReader.Read(path);

        Assert.NotNull(info);
        Assert.Equal("My Survival World", info!.LevelName);
        Assert.Equal("1.21.5", info.VersionName);
        Assert.Equal(0, info.GameType);
        Assert.NotNull(info.LastPlayedIso);

        // Round-trip the ISO string back to a DateTimeOffset and confirm the epoch math.
        var parsed = DateTimeOffset.Parse(info.LastPlayedIso!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(lastPlayedMs), parsed);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        var info = LevelDatReader.Read(Path.Combine(_temp, "absent.dat"));
        Assert.Null(info);
    }

    [Fact]
    public void Read_CorruptFile_ReturnsNull_NotThrow()
    {
        var path = Path.Combine(_temp, "corrupt.dat");
        File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var info = LevelDatReader.Read(path);

        Assert.Null(info);
    }

    [Fact]
    public void Read_NoVersionCompound_StillReturnsLevelName()
    {
        var path = Path.Combine(_temp, "old-world.dat");
        var data = new NbtCompound("Data")
        {
            new NbtString("LevelName", "Ancient World"),
            new NbtLong("LastPlayed", 0L),
        };
        var root = new NbtCompound("") { data };
        new NbtFile(root).SaveToFile(path, NbtCompression.GZip);

        var info = LevelDatReader.Read(path);

        Assert.NotNull(info);
        Assert.Equal("Ancient World", info!.LevelName);
        Assert.Null(info.VersionName);
    }
}
