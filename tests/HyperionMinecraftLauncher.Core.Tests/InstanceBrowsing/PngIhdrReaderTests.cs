using System.IO;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

public class PngIhdrReaderTests : System.IDisposable
{
    private readonly string _temp;

    public PngIhdrReaderTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-png-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public void TryReadSize_ValidIhdr_ReturnsDimensions()
    {
        var path = Path.Combine(_temp, "ok.png");
        WriteIhdrPng(path, 1920, 1080);

        var ok = PngIhdrReader.TryReadSize(path, out var w, out var h);

        Assert.True(ok);
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
    }

    [Fact]
    public void TryReadSize_BadSignature_ReturnsFalse()
    {
        var path = Path.Combine(_temp, "junk.png");
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4 });

        var ok = PngIhdrReader.TryReadSize(path, out var w, out var h);

        Assert.False(ok);
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void TryReadSize_MissingFile_ReturnsFalse()
    {
        var ok = PngIhdrReader.TryReadSize(Path.Combine(_temp, "absent.png"), out _, out _);
        Assert.False(ok);
    }

    private static void WriteIhdrPng(string path, int width, int height)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new byte[8 + 13];
        WriteBigEndianInt(ihdr, 0, 13);
        ihdr[4] = (byte)'I'; ihdr[5] = (byte)'H'; ihdr[6] = (byte)'D'; ihdr[7] = (byte)'R';
        WriteBigEndianInt(ihdr, 8, width);
        WriteBigEndianInt(ihdr, 12, height);
        ihdr[16] = 8;
        ihdr[17] = 2;
        ihdr[18] = 0;
        ihdr[19] = 0;
        ihdr[20] = 0;

        using var fs = File.Create(path);
        fs.Write(sig, 0, sig.Length);
        fs.Write(ihdr, 0, ihdr.Length);
    }

    private static void WriteBigEndianInt(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
