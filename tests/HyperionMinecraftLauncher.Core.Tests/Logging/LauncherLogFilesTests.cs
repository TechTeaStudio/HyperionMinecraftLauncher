using System;
using System.IO;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Logging;

public class LauncherLogFilesTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Local);
    private readonly string _root;

    public LauncherLogFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hypmcl-logfiles-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void FindForDate_MissingDirectory_ReturnsNull()
    {
        Assert.Null(LauncherLogFiles.FindForDate(Path.Combine(_root, "nope"), Day));
    }

    [Fact]
    public void FindForDate_SerilogDailyFile_IsFound()
    {
        // Serilog's RollingInterval.Day stamps "launcher-.log" as yyyyMMdd, not yyyy-MM-dd.
        var expected = Touch("launcher-20260922.log");

        Assert.Equal(expected, LauncherLogFiles.FindForDate(_root, Day));
    }

    [Fact]
    public void FindForDate_SizeRolledParts_ReturnsNewest()
    {
        Touch("launcher-20260922.log");
        Touch("launcher-20260922_001.log");
        var expected = Touch("launcher-20260922_002.log");

        Assert.Equal(expected, LauncherLogFiles.FindForDate(_root, Day));
    }

    [Fact]
    public void FindForDate_OtherDaysOnly_ReturnsNull()
    {
        Touch("launcher-20260921.log");
        Touch("launcher-20260923.log");

        Assert.Null(LauncherLogFiles.FindForDate(_root, Day));
    }

    [Fact]
    public void ExpectedPath_UsesSerilogStamp()
    {
        Assert.Equal(Path.Combine(_root, "launcher-20260922.log"), LauncherLogFiles.ExpectedPath(_root, Day));
    }

    [Fact]
    public void FindForDate_FileWrittenBySerilogLogger_IsFound()
    {
        using (var logger = new SerilogLauncherLogger(_root))
            logger.Info("hello");

        var found = LauncherLogFiles.FindForDate(_root, DateTime.Now);

        Assert.NotNull(found);
        Assert.Contains("hello", File.ReadAllText(found!));
    }
}
