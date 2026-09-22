using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Playtime;

public class GameProcessWatcherTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

    [Fact]
    public void IsRunning_CurrentProcessWithMatchingStart_IsTrue()
    {
        using var self = Process.GetCurrentProcess();
        var started = new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero);

        Assert.True(GameProcessWatcher.IsRunning(self.Id, started, Tolerance));
    }

    [Fact]
    public void IsRunning_PidReusedByAnotherProcess_IsFalse()
    {
        // Same pid, but the journaled start is days away from the live process: a reused pid.
        using var self = Process.GetCurrentProcess();
        var journaled = new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero).AddDays(-3);

        Assert.False(GameProcessWatcher.IsRunning(self.Id, journaled, Tolerance));
    }

    [Fact]
    public void IsRunning_UnknownPid_IsFalse()
    {
        Assert.False(GameProcessWatcher.IsRunning(int.MaxValue, DateTimeOffset.UtcNow, Tolerance));
    }

    [Fact]
    public async Task WaitForExit_UnknownPid_ReturnsImmediately()
    {
        var wait = GameProcessWatcher.WaitForExitAsync(int.MaxValue, CancellationToken.None);

        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task WaitForExit_CompletesWhenTheProcessExits()
    {
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? "/c ping -n 2 127.0.0.1 >NUL" : "-c \"sleep 1\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var wait = GameProcessWatcher.WaitForExitAsync(child.Id, CancellationToken.None);

        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(15))));
        Assert.True(child.HasExited);
    }
}
