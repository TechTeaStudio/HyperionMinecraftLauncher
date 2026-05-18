using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Import.MultiMc;

/// <summary>
/// End-to-end tests for <see cref="MultiMcInstanceImporter"/>: build a synthetic Prism /
/// MultiMC instance (both as a folder and as a zip), run the importer, and verify the
/// resulting <see cref="Instance"/> + extracted game-dir contents.
/// </summary>
public class MultiMcInstanceImporterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _importTargetRoot;

    public MultiMcInstanceImporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "HMLTests_MMC_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _importTargetRoot = Path.Combine(_tempRoot, "imported");
        Directory.CreateDirectory(_importTargetRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ImportAsync_ZipWithFabricInstance_ReturnsPopulatedInstanceAndCopiesGameDir()
    {
        var zipPath = BuildPrismZip(
            "fabric-pack.zip",
            cfg: """
                InstanceType=OneSix
                name=Awesome Fabric Pack
                iconKey=flame
                JvmArgs=-Xss2M
                MaxMemAlloc=8192
                MinMemAlloc=1024
                """,
            mmcPack: """
                {
                  "components": [
                    { "uid": "net.fabricmc.intermediary", "version": "1.21.4" },
                    { "uid": "net.fabricmc.fabric-loader", "version": "0.16.10" },
                    { "uid": "net.minecraft", "version": "1.21.4" }
                  ],
                  "formatVersion": 1
                }
                """,
            files: new[]
            {
                (".minecraft/mods/foo.jar", "jar-bytes"),
                (".minecraft/config/bar.toml", "config-bytes"),
                (".minecraft/options.txt", "fov:80"),
                (".minecraft/versions/should-be-ignored.json", "should not be copied"),
            });

        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(zipPath, overrideName: null, progress: null, CancellationToken.None);

        Assert.Equal("Awesome Fabric Pack", imported.Name);
        Assert.Equal("1.21.4", imported.VersionId);
        Assert.Equal(ModLoader.Fabric, imported.Loader);
        Assert.Equal("0.16.10", imported.LoaderVersion);
        Assert.Equal("-Xss2M", imported.JvmArguments);
        Assert.Equal(1024, imported.MinimumRamMb);
        Assert.Equal(8192, imported.MaximumRamMb);
        Assert.False(imported.IsAutoImported);
        Assert.False(string.IsNullOrWhiteSpace(imported.GameDirectory));
        Assert.True(Directory.Exists(imported.GameDirectory));

        // Game-dir contents copied; versions/ explicitly excluded.
        Assert.True(File.Exists(Path.Combine(imported.GameDirectory!, "mods", "foo.jar")));
        Assert.True(File.Exists(Path.Combine(imported.GameDirectory!, "config", "bar.toml")));
        Assert.True(File.Exists(Path.Combine(imported.GameDirectory!, "options.txt")));
        Assert.False(File.Exists(Path.Combine(imported.GameDirectory!, "versions", "should-be-ignored.json")));
    }

    [Fact]
    public async Task ImportAsync_FolderSource_AcceptsUnzippedInstance()
    {
        var folder = Path.Combine(_tempRoot, "folder-source");
        Directory.CreateDirectory(Path.Combine(folder, ".minecraft", "mods"));
        File.WriteAllText(Path.Combine(folder, "instance.cfg"),
            "name=Folder Pack\niconKey=stone\nMaxMemAlloc=4096\n");
        File.WriteAllText(Path.Combine(folder, "mmc-pack.json"), """
            {
              "components": [
                { "uid": "net.minecraft", "version": "1.20.1" },
                { "uid": "net.minecraftforge", "version": "47.4.5" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(folder, ".minecraft", "mods", "stuff.jar"), "stuff");

        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(folder, overrideName: null, progress: null, CancellationToken.None);

        Assert.Equal("Folder Pack", imported.Name);
        Assert.Equal("1.20.1", imported.VersionId);
        Assert.Equal(ModLoader.Forge, imported.Loader);
        Assert.Equal("47.4.5", imported.LoaderVersion);
        Assert.Equal(InstanceIcons.Stone, imported.IconKey);
        Assert.Equal(4096, imported.MaximumRamMb);
        Assert.Null(imported.MinimumRamMb);
        Assert.True(File.Exists(Path.Combine(imported.GameDirectory!, "mods", "stuff.jar")));
    }

    [Fact]
    public async Task ImportAsync_OverrideName_BeatsCfgName()
    {
        var folder = BuildPrismFolder("override-source", "name=OriginalName", VanillaPackJson("1.21.0"));
        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(folder, overrideName: "NewName", progress: null, CancellationToken.None);

        Assert.Equal("NewName", imported.Name);
    }

    [Fact]
    public async Task ImportAsync_MissingCfgName_FallsBackToDefault()
    {
        var folder = BuildPrismFolder("no-name", "InstanceType=OneSix", VanillaPackJson("1.20.4"));
        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(folder, overrideName: null, progress: null, CancellationToken.None);

        Assert.Equal("Imported from MultiMC", imported.Name);
        Assert.Equal(InstanceIcons.GrassBlock, imported.IconKey);
    }

    [Fact]
    public async Task ImportAsync_ZipMissingInstanceCfg_Throws()
    {
        var zipPath = Path.Combine(_tempRoot, "no-cfg.zip");
        using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(archive, "mmc-pack.json", VanillaPackJson("1.21.0"));
        }

        var importer = new MultiMcInstanceImporter(_importTargetRoot);

        var ex = await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(zipPath, null, null, CancellationToken.None));
        Assert.Contains("instance.cfg", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_ZipMissingMmcPack_Throws()
    {
        var zipPath = Path.Combine(_tempRoot, "no-pack.zip");
        using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(archive, "instance.cfg", "name=NoPack\n");
        }

        var importer = new MultiMcInstanceImporter(_importTargetRoot);

        var ex = await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(zipPath, null, null, CancellationToken.None));
        Assert.Contains("mmc-pack.json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_MalformedPackJson_Throws()
    {
        var folder = BuildPrismFolder("malformed", "name=Broken", "{ not valid json");
        var importer = new MultiMcInstanceImporter(_importTargetRoot);

        await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(folder, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_ZipNestedUnderInstanceFolder_StillResolves()
    {
        // Prism's export wraps the whole instance under a single named root inside the zip.
        var zipPath = Path.Combine(_tempRoot, "nested.zip");
        using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(archive, "MyPack/instance.cfg", "name=Nested\niconKey=compass\n");
            AddEntry(archive, "MyPack/mmc-pack.json", VanillaPackJson("1.21.0"));
            AddEntry(archive, "MyPack/.minecraft/mods/m.jar", "mod");
        }

        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(zipPath, null, null, CancellationToken.None);

        Assert.Equal("Nested", imported.Name);
        Assert.Equal(InstanceIcons.Compass, imported.IconKey);
        Assert.True(File.Exists(Path.Combine(imported.GameDirectory!, "mods", "m.jar")));
    }

    [Fact]
    public async Task ImportAsync_NonExistentSource_Throws()
    {
        var importer = new MultiMcInstanceImporter(_importTargetRoot);
        await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(Path.Combine(_tempRoot, "definitely-missing.zip"), null, null, CancellationToken.None));
    }

    // -------------------- helpers --------------------

    private string BuildPrismFolder(string folderName, string cfg, string mmcPack)
    {
        var folder = Path.Combine(_tempRoot, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "instance.cfg"), cfg);
        File.WriteAllText(Path.Combine(folder, "mmc-pack.json"), mmcPack);
        return folder;
    }

    private string BuildPrismZip(string name, string cfg, string mmcPack, (string Path, string Body)[] files)
    {
        var zipPath = Path.Combine(_tempRoot, name);
        using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        AddEntry(archive, "instance.cfg", cfg);
        AddEntry(archive, "mmc-pack.json", mmcPack);
        foreach (var (path, body) in files)
            AddEntry(archive, path, body);
        return zipPath;
    }

    private static void AddEntry(ZipArchive archive, string path, string body)
    {
        var entry = archive.CreateEntry(path);
        using var s = entry.Open();
        using var sw = new StreamWriter(s);
        sw.Write(body);
    }

    private static string VanillaPackJson(string mcVersion)
    {
        return $$"""
            {
              "components": [
                { "uid": "net.minecraft", "version": "{{mcVersion}}" }
              ]
            }
            """;
    }
}
