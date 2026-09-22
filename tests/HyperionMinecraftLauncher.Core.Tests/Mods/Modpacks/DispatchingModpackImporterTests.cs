using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods.Modpacks;

public class DispatchingModpackImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hyperion-dispatch-test-" + Guid.NewGuid().ToString("N"));

    public DispatchingModpackImporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Zip(string name, string entry, string json)
    {
        var path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry(entry).Open(), Encoding.UTF8);
        w.Write(json);
        return path;
    }

    [Fact]
    public void ReadManifest_Mrpack_ReturnsPackMetadataWithoutDownloading()
    {
        var path = Zip("pack.mrpack", "modrinth.index.json", """
            { "name": "Nestle Optimized", "versionId": "1.1.0",
              "dependencies": { "minecraft": "26.1.2", "fabric-loader": "0.19.3" },
              "files": [ { "path": "mods/a.jar", "downloads": ["https://cdn.example/a.jar"] } ] }
            """);

        var manifest = DispatchingModpackImporter.ReadManifest(path);

        Assert.Equal("Nestle Optimized", manifest.Name);
        Assert.Equal("1.1.0", manifest.Version);
        Assert.Equal("26.1.2", manifest.MinecraftVersion);
        Assert.Equal(ModLoader.Fabric, manifest.Loader);
        Assert.Equal("0.19.3", manifest.LoaderVersion);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void ReadManifest_CurseForgeZip_ReturnsPackMetadata()
    {
        var path = Zip("cf.zip", "manifest.json", """
            { "minecraft": { "version": "1.20.1", "modLoaders": [ { "id": "forge-47.2.0", "primary": true } ] },
              "manifestType": "minecraftModpack", "manifestVersion": 1,
              "name": "Cool Pack", "version": "1.4.2", "files": [], "overrides": "overrides" }
            """);

        var manifest = DispatchingModpackImporter.ReadManifest(path);

        Assert.Equal("Cool Pack", manifest.Name);
        Assert.Equal(ModLoader.Forge, manifest.Loader);
    }

    [Fact]
    public void ReadManifest_PlainZip_ThrowsNotSupported()
    {
        var path = Zip("export.zip", "instance.json", "{}");

        Assert.Throws<NotSupportedException>(() => DispatchingModpackImporter.ReadManifest(path));
    }
}
