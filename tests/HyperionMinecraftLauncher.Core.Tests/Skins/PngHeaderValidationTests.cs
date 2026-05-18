using System;
using System.Linq;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

public class PngHeaderValidationTests
{
    [Fact]
    public void HasValidSignature_GenuinePng_ReturnsTrue()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xAB };
        Assert.True(PngHeader.HasValidSignature(data));
    }

    [Fact]
    public void HasValidSignature_TruncatedHeader_ReturnsFalse()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        Assert.False(PngHeader.HasValidSignature(data));
    }

    [Fact]
    public void HasValidSignature_EmptyBuffer_ReturnsFalse()
    {
        Assert.False(PngHeader.HasValidSignature(Array.Empty<byte>()));
    }

    [Fact]
    public void HasValidSignature_JpegHeader_ReturnsFalse()
    {
        // JPEG magic + padding to 8 bytes.
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00 };
        Assert.False(PngHeader.HasValidSignature(data));
    }

    [Fact]
    public void IsAcceptableSkinPng_OverSizeLimit_ReturnsFalse()
    {
        var data = Enumerable.Range(0, 33 * 1024)
            .Select(i => i == 0 ? (byte)0x89 : i == 1 ? (byte)0x50 : (byte)0)
            .ToArray();
        // Patch in the proper header
        var hdr = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(hdr, data, hdr.Length);
        Assert.False(PngHeader.IsAcceptableSkinPng(data));
    }

    [Fact]
    public void IsAcceptableSkinPng_TinyValidPng_ReturnsTrue()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(PngHeader.IsAcceptableSkinPng(data));
    }
}
