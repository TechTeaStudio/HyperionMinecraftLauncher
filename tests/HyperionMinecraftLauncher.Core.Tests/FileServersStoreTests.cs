using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using fNbt;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class FileServersStoreTests : IDisposable
{
    private readonly string _temp;
    private readonly string _path;

    public FileServersStoreTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-servers-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
        _path = Path.Combine(_temp, "servers.dat");
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private static void WriteServersFile(string path, params (string name, string ip, string? icon, bool? accept)[] entries)
    {
        var list = new NbtList("servers", NbtTagType.Compound);
        foreach (var (name, ip, icon, accept) in entries)
        {
            var c = new NbtCompound
            {
                new NbtString("name", name),
                new NbtString("ip", ip),
            };
            if (icon is not null) c.Add(new NbtString("icon", icon));
            if (accept is bool b) c.Add(new NbtByte("acceptTextures", (byte)(b ? 1 : 0)));
            list.Add(c);
        }
        var root = new NbtCompound("") { list };
        var file = new NbtFile(root);
        file.SaveToFile(path, NbtCompression.None);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var result = await new FileServersStore().LoadAsync(_path, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_SimpleEntries_ParsesNameAndIp()
    {
        WriteServersFile(_path,
            ("Hypixel", "mc.hypixel.net", null, null),
            ("Local", "localhost:25565", null, null));

        var result = await new FileServersStore().LoadAsync(_path, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Hypixel", result[0].Name);
        Assert.Equal("mc.hypixel.net", result[0].Ip);
        Assert.Equal("Local", result[1].Name);
        Assert.Equal("localhost:25565", result[1].Ip);
    }

    [Fact]
    public async Task LoadAsync_OptionalFields_RoundTripCorrectly()
    {
        WriteServersFile(_path,
            ("WithIcon", "mc.icon", "iVBORw0KGgo=", true),
            ("WithoutIcon", "mc.bare", null, false));

        var result = await new FileServersStore().LoadAsync(_path, CancellationToken.None);

        Assert.Equal("iVBORw0KGgo=", result[0].IconBase64);
        Assert.True(result[0].AcceptTextures);
        Assert.Null(result[1].IconBase64);
        Assert.False(result[1].AcceptTextures);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmpty_NotThrow()
    {
        File.WriteAllBytes(_path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x42 });

        var result = await new FileServersStore().LoadAsync(_path, CancellationToken.None);

        Assert.Empty(result);
    }
}
