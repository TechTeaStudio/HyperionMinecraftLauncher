using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Export;

/// <summary>
/// Verifies the file-backed exporter produces a Hyperion-format zip with the right entries.
/// The harness writes a synthetic instance folder (mods/, config/, saves/, screenshots/),
/// calls the exporter twice (defaults vs. IncludeSaves=true), and inspects the resulting
/// zip's central directory.
/// </summary>
public class FileInstanceExporterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _gameDir;
    private readonly Instance _instance;

    public FileInstanceExporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "HMLTests_Exporter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _gameDir = Path.Combine(_tempRoot, "minecraft");
        Directory.CreateDirectory(Path.Combine(_gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "config"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "saves", "MyWorld"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "screenshots"));
        File.WriteAllText(Path.Combine(_gameDir, "mods", "fancy.jar"), "jar-bytes");
        File.WriteAllText(Path.Combine(_gameDir, "config", "fancy.toml"), "config-bytes");
        File.WriteAllText(Path.Combine(_gameDir, "saves", "MyWorld", "level.dat"), "world-bytes");
        File.WriteAllText(Path.Combine(_gameDir, "screenshots", "shot.png"), "png-bytes");

        _instance = new Instance
        {
            Id = "11111111111111111111111111111111",
            Name = "ExportSubject",
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
    public async Task ExportAsync_Defaults_IncludesModsAndConfigButExcludesSavesAndScreenshots()
    {
        var zipPath = Path.Combine(_tempRoot, "out-defaults.zip");
        var exporter = new FileInstanceExporter();

        await exporter.ExportAsync(_instance, zipPath, progress: null, CancellationToken.None);

        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();

        Assert.Contains("hyperion-instance.json", names);
        Assert.Contains("metadata.json", names);
        Assert.Contains(names, n => n.StartsWith("gameDir/mods/", StringComparison.Ordinal) && n.EndsWith("fancy.jar", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("gameDir/config/", StringComparison.Ordinal) && n.EndsWith("fancy.toml", StringComparison.Ordinal));

        Assert.DoesNotContain(names, n => n.StartsWith("gameDir/saves/", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("gameDir/screenshots/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_HyperionInstanceJson_RoundTripsTheRecord()
    {
        var zipPath = Path.Combine(_tempRoot, "out-record.zip");
        var exporter = new FileInstanceExporter();

        await exporter.ExportAsync(_instance, zipPath, progress: null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("hyperion-instance.json");
        Assert.NotNull(entry);
        await using var stream = entry!.Open();
        var written = await JsonSerializer.DeserializeAsync<Instance>(stream);
        Assert.NotNull(written);
        Assert.Equal(_instance.Id, written!.Id);
        Assert.Equal(_instance.Name, written.Name);
        Assert.Equal(_instance.VersionId, written.VersionId);
        Assert.Equal(_instance.IconKey, written.IconKey);
    }

    [Fact]
    public async Task ExportAsync_MetadataJson_HasFormatVersion1()
    {
        var zipPath = Path.Combine(_tempRoot, "out-meta.zip");
        var exporter = new FileInstanceExporter();

        await exporter.ExportAsync(_instance, zipPath, progress: null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("metadata.json");
        Assert.NotNull(entry);
        await using var stream = entry!.Open();
        var meta = await JsonSerializer.DeserializeAsync<InstanceExportMetadata>(stream);
        Assert.NotNull(meta);
        Assert.Equal("1", meta!.FormatVersion);
        Assert.False(string.IsNullOrWhiteSpace(meta.ExporterVersion));
    }

    [Fact]
    public async Task ExportAsync_IncludeSavesTrue_BundlesSavesFolder()
    {
        var zipPath = Path.Combine(_tempRoot, "out-with-saves.zip");
        var exporter = new FileInstanceExporter();

        await exporter.ExportAsync(_instance, zipPath, new ExportOptions(IncludeSaves: true), progress: null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        Assert.Contains(names, n => n.StartsWith("gameDir/saves/", StringComparison.Ordinal) && n.EndsWith("level.dat", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_ReportsProgress_BetweenZeroAndOneInclusive()
    {
        var zipPath = Path.Combine(_tempRoot, "out-progress.zip");
        var exporter = new FileInstanceExporter();
        var samples = new System.Collections.Generic.List<double>();
        var progress = new Progress<double>(v => samples.Add(v));

        await exporter.ExportAsync(_instance, zipPath, progress, CancellationToken.None);

        // Progress reporter is best-effort but should fire at least once and stay in [0,1].
        Assert.All(samples, v => Assert.InRange(v, 0.0, 1.0));
    }
}
