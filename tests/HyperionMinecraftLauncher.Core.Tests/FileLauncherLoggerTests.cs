using System;
using System.IO;
using System.Linq;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

// FileLauncherLogger was marked [Obsolete] in v0.28.0 (keep-for-tests). These tests
// intentionally exercise it, so suppress CS0618 for this file only.
#pragma warning disable CS0618
public class FileLauncherLoggerTests : IDisposable
{
    private readonly string _root;

    public FileLauncherLoggerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hypmcl-logger-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Constructor_CreatesDirectoryEagerly()
    {
        var target = Path.Combine(_root, "logs");
        Assert.False(Directory.Exists(target));

        using var logger = new FileLauncherLogger(target);

        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Constructor_EmptyDirectoryThrows()
    {
        Assert.Throws<ArgumentException>(() => new FileLauncherLogger(""));
    }

    [Fact]
    public void Info_WritesOneLineToTodayFile()
    {
        var target = Path.Combine(_root, "logs");
        var fixedClock = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        using var logger = new FileLauncherLogger(target, () => fixedClock);

        logger.Info("hello world");

        var expected = Path.Combine(target, "launcher-2026-05-15.log");
        Assert.True(File.Exists(expected));
        var content = File.ReadAllText(expected);
        Assert.Contains("[INFO]", content);
        Assert.Contains("hello world", content);
        Assert.Contains("2026-05-15 12:00:00", content);
    }

    [Fact]
    public void Error_WithException_WritesStackOnNextLine()
    {
        var target = Path.Combine(_root, "logs");
        var fixedClock = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        using var logger = new FileLauncherLogger(target, () => fixedClock);

        Exception captured;
        try { throw new InvalidOperationException("boom"); } catch (Exception e) { captured = e; }
        logger.Error("operation failed", captured);

        var file = Path.Combine(target, "launcher-2026-05-15.log");
        var content = File.ReadAllText(file);
        var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        Assert.True(lines.Length >= 2, $"Expected at least 2 lines, got {lines.Length}: {content}");
        Assert.Contains("[ERROR]", lines[0]);
        Assert.Contains("operation failed", lines[0]);
        // Stack trace is one logical line that may wrap; the first wrapped line starts with "    at "
        Assert.StartsWith("    at ", lines[1]);
        Assert.Contains("InvalidOperationException", lines[1]);
    }

    [Fact]
    public void Rotation_NewDayCreatesNewFile()
    {
        var target = Path.Combine(_root, "logs");
        var current = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        using var logger = new FileLauncherLogger(target, () => current);

        logger.Info("day-1 line");

        current = new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.Zero);
        logger.Info("day-2 line");

        var day1 = Path.Combine(target, "launcher-2026-05-15.log");
        var day2 = Path.Combine(target, "launcher-2026-05-16.log");
        Assert.True(File.Exists(day1));
        Assert.True(File.Exists(day2));

        Assert.Contains("day-1 line", File.ReadAllText(day1));
        Assert.DoesNotContain("day-2 line", File.ReadAllText(day1));
        Assert.Contains("day-2 line", File.ReadAllText(day2));
    }

    [Fact]
    public void Multiple_Calls_AppendsToSameFile()
    {
        var target = Path.Combine(_root, "logs");
        var fixedClock = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        using var logger = new FileLauncherLogger(target, () => fixedClock);

        logger.Info("first");
        logger.Warn("second");
        logger.Info("third");

        var file = Path.Combine(target, "launcher-2026-05-15.log");
        var lines = File.ReadAllLines(file);
        Assert.Equal(3, lines.Length);
        Assert.Contains("[INFO] first", lines[0]);
        Assert.Contains("[WARN] second", lines[1]);
        Assert.Contains("[INFO] third", lines[2]);
    }

    [Fact]
    public void DefaultLogDirectory_Resolve_EndsWithHyperionLogs()
    {
        var path = DefaultLogDirectory.Resolve();

        Assert.False(string.IsNullOrEmpty(path));
        // Use Path.Combine to make the assertion separator-agnostic.
        var tail = Path.Combine(DefaultLogDirectory.ApplicationName, DefaultLogDirectory.LogsSubdirectory);
        Assert.EndsWith(tail, path);
    }
}
#pragma warning restore CS0618
