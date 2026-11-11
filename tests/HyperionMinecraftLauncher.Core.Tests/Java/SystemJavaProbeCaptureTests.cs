using System.Diagnostics;
using System.Runtime.InteropServices;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Java;

/// <summary>
/// Tests for <see cref="SystemJavaProbe.CaptureOutputAsync"/>, the one place the probe starts a
/// process. They run real, harmless commands of the host (the shell and the dotnet host that is
/// running the tests), so no Java has to be installed.
/// </summary>
public class SystemJavaProbeCaptureTests
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static string Shell => IsWindows ? "cmd.exe" : "/bin/sh";

    private static string[] ShellCommand(string command) =>
        IsWindows ? new[] { "/c", command } : new[] { "-c", command };

    [Fact]
    public async Task CaptureOutputAsync_ReadsStandardOutput()
    {
        var text = await SystemJavaProbe.CaptureOutputAsync(
            Shell, ShellCommand("echo to-stdout"), Generous, CancellationToken.None);

        Assert.NotNull(text);
        Assert.Contains("to-stdout", text);
    }

    [Fact]
    public async Task CaptureOutputAsync_ReadsStandardError()
    {
        // java prints its version banner to stderr, so this is the stream that matters.
        var text = await SystemJavaProbe.CaptureOutputAsync(
            Shell, ShellCommand("echo to-stderr 1>&2"), Generous, CancellationToken.None);

        Assert.NotNull(text);
        Assert.Contains("to-stderr", text);
    }

    [Fact]
    public async Task CaptureOutputAsync_NonZeroExitCode_StillReturnsTheText()
    {
        var text = await SystemJavaProbe.CaptureOutputAsync(
            Shell, ShellCommand(IsWindows ? "echo before-exit& exit 3" : "echo before-exit; exit 3"),
            Generous, CancellationToken.None);

        Assert.NotNull(text);
        Assert.Contains("before-exit", text);
    }

    [Fact]
    public async Task CaptureOutputAsync_MissingExecutable_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-java-" + Guid.NewGuid().ToString("N"), "java.exe");

        var text = await SystemJavaProbe.CaptureOutputAsync(
            missing, new[] { "-version" }, Generous, CancellationToken.None);

        Assert.Null(text);
    }

    [Fact]
    public async Task CaptureOutputAsync_ProcessOutlivesTimeout_ReturnsNullPromptly()
    {
        var (exe, args) = SleepCommand(seconds: 30);
        var watch = Stopwatch.StartNew();

        var text = await SystemJavaProbe.CaptureOutputAsync(
            exe, args, TimeSpan.FromMilliseconds(400), CancellationToken.None);

        watch.Stop();
        Assert.Null(text);
        // The process would run for 30 s: returning well before that proves it was not waited out.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task CaptureOutputAsync_CallerCancels_Throws()
    {
        var (exe, args) = SleepCommand(seconds: 30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SystemJavaProbe.CaptureOutputAsync(exe, args, Generous, cts.Token));
    }

    private static (string Exe, string[] Args) SleepCommand(int seconds) =>
        IsWindows
            ? ("ping.exe", new[] { "-n", (seconds + 1).ToString(), "127.0.0.1" })
            : ("/bin/sh", new[] { "-c", $"sleep {seconds}" });
}
