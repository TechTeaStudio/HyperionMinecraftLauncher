using System;
using System.IO;
using System.Linq;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Logging;

public class SerilogLauncherLoggerTests : IDisposable
{
    private readonly string _root;

    public SerilogLauncherLoggerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hypmcl-serilog-tests-" + Guid.NewGuid().ToString("N"));
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
    public void Constructor_EmptyDirectoryThrows()
    {
        Assert.Throws<ArgumentException>(() => new SerilogLauncherLogger(""));
    }

    [Fact]
    public void Constructor_CreatesDirectoryEagerly()
    {
        var target = Path.Combine(_root, "logs");
        Assert.False(Directory.Exists(target));

        using var logger = new SerilogLauncherLogger(target);

        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void ThreeEntries_CreateFileWithAllMessages()
    {
        var target = Path.Combine(_root, "logs");
        SerilogLauncherLogger logger = new(target);

        logger.Info("first-info-line");
        logger.Warn("second-warn-line");
        logger.Info("third-info-line");

        // Dispose flushes - we need this to read the file synchronously.
        logger.Dispose();

        var files = Directory.GetFiles(target, "launcher-*.log");
        Assert.NotEmpty(files);

        var content = string.Join('\n', files.Select(File.ReadAllText));
        Assert.Contains("first-info-line", content);
        Assert.Contains("second-warn-line", content);
        Assert.Contains("third-info-line", content);
    }

    [Fact]
    public void Error_WithException_WritesMessageAndStackTrace()
    {
        var target = Path.Combine(_root, "logs");
        SerilogLauncherLogger logger = new(target);

        Exception captured;
        try { throw new InvalidOperationException("kaboom-marker-7af3"); }
        catch (Exception e) { captured = e; }

        logger.Error("operation-error-marker-91bd", captured);
        logger.Dispose();

        var files = Directory.GetFiles(target, "launcher-*.log");
        Assert.NotEmpty(files);

        var content = string.Join('\n', files.Select(File.ReadAllText));
        Assert.Contains("operation-error-marker-91bd", content);
        // Stack trace must contain the exception type name (InvalidOperationException)
        // and the message that was thrown.
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("kaboom-marker-7af3", content);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var target = Path.Combine(_root, "logs");
        var logger = new SerilogLauncherLogger(target);
        logger.Info("anything");

        logger.Dispose();
        // Calling a second time must not throw.
        logger.Dispose();
    }
}
