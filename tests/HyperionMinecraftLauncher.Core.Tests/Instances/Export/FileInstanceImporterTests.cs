using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Export;

/// <summary>
/// Round-trip tests for <see cref="FileInstanceImporter"/>: build an instance, export it,
/// re-import it, and verify the resulting instance + extracted game-dir match. Also covers
/// the "obviously invalid zip" rejection path the UI relies on for friendly errors.
/// </summary>
public class FileInstanceImporterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _gameDir;
    private readonly string _importTargetRoot;
    private readonly Instance _instance;

    public FileInstanceImporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "HMLTests_Importer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _gameDir = Path.Combine(_tempRoot, "gameDir");
        Directory.CreateDirectory(Path.Combine(_gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "config"));
        File.WriteAllText(Path.Combine(_gameDir, "mods", "fancy.jar"), "jar-bytes");
        File.WriteAllText(Path.Combine(_gameDir, "config", "fancy.toml"), "config-bytes");
        File.WriteAllText(Path.Combine(_gameDir, "options.txt"), "fov:80");

        _importTargetRoot = Path.Combine(_tempRoot, "imported");
        Directory.CreateDirectory(_importTargetRoot);

        _instance = new Instance
        {
            Id = "11111111111111111111111111111111",
            Name = "ImportSubject",
            VersionId = "1.20.4",
            IconKey = InstanceIcons.Compass,
            GameDirectory = _gameDir,
        };
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ImportAsync_RoundTrip_ReturnsInstanceWithNewIdAndExtractsGameDir()
    {
        var zipPath = Path.Combine(_tempRoot, "round-trip.zip");
        var exporter = new FileInstanceExporter();
        await exporter.ExportAsync(_instance, zipPath, progress: null, CancellationToken.None);

        var importer = new FileInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(zipPath, overrideName: null, progress: null, CancellationToken.None);

        Assert.NotNull(imported);
        Assert.NotEqual(_instance.Id, imported.Id); // fresh Id
        Assert.Equal(_instance.Name, imported.Name);
        Assert.Equal(_instance.VersionId, imported.VersionId);
        Assert.Equal(_instance.IconKey, imported.IconKey);
        Assert.False(string.IsNullOrWhiteSpace(imported.GameDirectory));
        Assert.True(Directory.Exists(imported.GameDirectory));

        // Game-dir contents end up at <importTargetRoot>/<NewId>/.../...
        var importedJar = Path.Combine(imported.GameDirectory!, "mods", "fancy.jar");
        var importedConfig = Path.Combine(imported.GameDirectory!, "config", "fancy.toml");
        var importedOptions = Path.Combine(imported.GameDirectory!, "options.txt");
        Assert.True(File.Exists(importedJar));
        Assert.True(File.Exists(importedConfig));
        Assert.True(File.Exists(importedOptions));
    }

    [Fact]
    public async Task ImportAsync_OverrideName_RenamesImportedInstance()
    {
        var zipPath = Path.Combine(_tempRoot, "rename.zip");
        var exporter = new FileInstanceExporter();
        await exporter.ExportAsync(_instance, zipPath, progress: null, CancellationToken.None);

        var importer = new FileInstanceImporter(_importTargetRoot);
        var imported = await importer.ImportAsync(zipPath, overrideName: "ShinyNew", progress: null, CancellationToken.None);

        Assert.Equal("ShinyNew", imported.Name);
    }

    [Fact]
    public async Task ImportAsync_ZipMissingHyperionInstanceJson_ThrowsFriendlyError()
    {
        var zipPath = Path.Combine(_tempRoot, "no-manifest.zip");
        using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            await using var s = entry.Open();
            await s.WriteAsync(Encoding.UTF8.GetBytes("not a hyperion instance zip"));
        }

        var importer = new FileInstanceImporter(_importTargetRoot);

        var ex = await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(zipPath, overrideName: null, progress: null, CancellationToken.None));

        Assert.Contains("hyperion-instance.json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_NonZipFile_ThrowsFriendlyError()
    {
        var bogusPath = Path.Combine(_tempRoot, "not-a-zip.zip");
        File.WriteAllText(bogusPath, "definitely not a zip");

        var importer = new FileInstanceImporter(_importTargetRoot);

        await Assert.ThrowsAsync<InstanceImportException>(
            () => importer.ImportAsync(bogusPath, overrideName: null, progress: null, CancellationToken.None));
    }
}
