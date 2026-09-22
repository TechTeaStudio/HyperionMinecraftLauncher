using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

public class PackMetadataReaderTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hyperion-packs-" + Guid.NewGuid().ToString("N"));

    public PackMetadataReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Zip(string name, params (string Entry, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, bytes) in entries)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(bytes);
        }
        return path;
    }

    private static byte[] Utf8(string s, bool bom = false)
        => (bom ? Encoding.UTF8.GetPreamble() : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(s)).ToArray();

    [Fact]
    public void ResourcePack_ReadsIconDescriptionAndFormat()
    {
        var path = Zip("Better-Leaves.zip",
            ("pack.png", Png),
            ("pack.mcmeta", Utf8("""{"pack":{"pack_format":15,"description":"§2Version 9.5"}}""", bom: true)));

        var meta = PackMetadataReader.Read(path, PackKind.Resource);

        Assert.Equal(Png, meta.IconPng);
        Assert.Equal(15, meta.PackFormat);
        Assert.Equal(("Version 9.5", "#00AA00"), (meta.Description.Single().Text, meta.Description.Single().Color));
    }

    [Fact]
    public void MissingIcon_AndBrokenMcmeta_GiveEmptyMetadataInsteadOfThrowing()
    {
        var path = Zip("broken.zip", ("pack.mcmeta", Utf8("{ not json")));

        var meta = PackMetadataReader.Read(path, PackKind.Resource);

        Assert.Null(meta.IconPng);
        Assert.Empty(meta.Description);
        Assert.Null(meta.PackFormat);
    }

    [Theory]
    [InlineData("", "shaders/logo.png")]
    [InlineData("", "pack.png")]
    [InlineData("BSL_v8.4/", "BSL_v8.4/shaders/logo.png")] // zipped together with its top folder
    public void ShaderPack_FindsAnImageWhereverPacksUsuallyKeepIt(string root, string entry)
    {
        var path = Zip("shader.zip", (root + "shaders/final.fsh", Utf8("void main(){}")), (entry, Png));

        Assert.Equal(Png, PackMetadataReader.Read(path, PackKind.Shader).IconPng);
    }

    [Fact]
    public void ShaderPack_WithoutImages_HasNoIcon()
    {
        var path = Zip("plain-shader.zip", ("shaders/final.fsh", Utf8("void main(){}")));

        Assert.Null(PackMetadataReader.Read(path, PackKind.Shader).IconPng);
    }

    [Fact]
    public void OversizedIcon_IsIgnored()
    {
        var huge = new byte[PackMetadataReader.MaxIconBytes + 1];
        var path = Zip("huge.zip", ("pack.png", huge));

        Assert.Null(PackMetadataReader.Read(path, PackKind.Resource).IconPng);
    }

    [Fact]
    public void NotAZip_GivesEmptyMetadata()
    {
        var path = Path.Combine(_dir, "fake.zip");
        File.WriteAllText(path, "definitely not a zip");

        var meta = PackMetadataReader.Read(path, PackKind.Resource);

        Assert.Null(meta.IconPng);
        Assert.Empty(meta.Description);
    }
}
