using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.CrashReports;

/// <summary>
/// Builds a temporary gameDir with two synthetic crash report files at different timestamps
/// and verifies the listener orders newest-first and honors the maxCount cap.
/// </summary>
public class FileSystemCrashReportListenerTests : IDisposable
{
    private readonly string _root;
    private readonly Instance _instance;

    public FileSystemCrashReportListenerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyperion-crashes-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _instance = new Instance { Id = "t", Name = "Test", VersionId = "1.21.5", GameDirectory = _root };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListRecentAsync_OrdersByGeneratedAtDescending()
    {
        var dir = Path.Combine(_root, "crash-reports");
        Directory.CreateDirectory(dir);

        // Older crash + newer crash. Filenames embed the timestamp - the parser uses that.
        var oldPath = Path.Combine(dir, "crash-2023-01-01_10.00.00-client.txt");
        var newPath = Path.Combine(dir, "crash-2024-08-12_14.05.33-client.txt");
        await File.WriteAllTextAsync(oldPath, SmallFixture("1.19.4"));
        await File.WriteAllTextAsync(newPath, SmallFixture("1.20.1"));

        var listener = new FileSystemCrashReportListener();
        var result = await listener.ListRecentAsync(_instance, maxCount: 20, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("crash-2024-08-12_14.05.33-client.txt", result[0].Filename);
        Assert.Equal("crash-2023-01-01_10.00.00-client.txt", result[1].Filename);
        Assert.Equal("1.20.1", result[0].MinecraftVersion);
    }

    [Fact]
    public async Task ListRecentAsync_HonorsMaxCount()
    {
        var dir = Path.Combine(_root, "crash-reports");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "crash-2023-01-01_10.00.00-client.txt"), SmallFixture("1.19.4"));
        await File.WriteAllTextAsync(Path.Combine(dir, "crash-2024-08-12_14.05.33-client.txt"), SmallFixture("1.20.1"));
        await File.WriteAllTextAsync(Path.Combine(dir, "crash-2025-01-15_09.30.00-client.txt"), SmallFixture("1.21"));

        var listener = new FileSystemCrashReportListener();
        var result = await listener.ListRecentAsync(_instance, maxCount: 2, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("crash-2025-01-15_09.30.00-client.txt", result[0].Filename);
        Assert.Equal("crash-2024-08-12_14.05.33-client.txt", result[1].Filename);
    }

    [Fact]
    public async Task ListRecentAsync_NoDirectory_ReturnsEmpty()
    {
        var listener = new FileSystemCrashReportListener();
        var result = await listener.ListRecentAsync(_instance, 20, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListRecentAsync_MaxCountZero_ReturnsEmpty()
    {
        var dir = Path.Combine(_root, "crash-reports");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "crash-2025-01-15_09.30.00-client.txt"), SmallFixture("1.21"));

        var listener = new FileSystemCrashReportListener();
        var result = await listener.ListRecentAsync(_instance, 0, CancellationToken.None);
        Assert.Empty(result);
    }

    private static string SmallFixture(string version) =>
        $"---- Minecraft Crash Report ----\n// hello\n\nTime: 2024-01-01 00:00:00\n\n-- System Details --\nDetails:\n\tMinecraft Version: {version}\n";
}
