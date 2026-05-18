using System.IO;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers;

/// <summary>
/// Round-trip the VarInt encoder/decoder for the boundary widths Minecraft's protocol uses
/// (0..127 fit in 1 byte, 128..16383 in 2, 16384..2097151 in 3, etc.). The negative case
/// proves we treat the input as a signed 32-bit value the way wiki.vg specifies (-1 encodes
/// as five 0xFF/0x0F bytes).
/// </summary>
public class VarIntTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(-1, 5)]
    public void RoundTrip(int value, int expectedByteLength)
    {
        using var ms = new MemoryStream();
        VarInt.Write(ms, value);
        Assert.Equal(expectedByteLength, ms.Length);

        ms.Position = 0;
        var decoded = VarInt.Read(ms);
        Assert.Equal(value, decoded);
        Assert.Equal(ms.Length, ms.Position);
    }

    [Fact]
    public void Write_KnownBytes_Zero()
    {
        using var ms = new MemoryStream();
        VarInt.Write(ms, 0);
        Assert.Equal(new byte[] { 0x00 }, ms.ToArray());
    }

    [Fact]
    public void Write_KnownBytes_OneTwentyEight()
    {
        // 128 = 0b1000_0000 -> two bytes: 0x80 0x01
        using var ms = new MemoryStream();
        VarInt.Write(ms, 128);
        Assert.Equal(new byte[] { 0x80, 0x01 }, ms.ToArray());
    }
}
