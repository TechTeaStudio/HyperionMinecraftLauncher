using System;
using System.IO;
using System.Runtime.InteropServices;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Platform;

/// <summary>
/// Verifies <see cref="DefaultLogDirectory.Resolve"/> picks a sensible platform-specific
/// location: XDG state on Linux, %LOCALAPPDATA%/logs on Windows, ~/Library/Logs on macOS.
/// </summary>
public class DefaultLogDirectoryTests
{
    [Fact]
    public void Resolve_Linux_WithXdgStateHomeSet_UsesXdgStateHome()
    {
        var env = new FakeEnvironment
        {
            CurrentPlatform = OSPlatform.Linux,
        }
        .SetVar("XDG_STATE_HOME", "/custom/state")
        .SetFolder(Environment.SpecialFolder.UserProfile, "/home/tester");

        var path = DefaultLogDirectory.Resolve(env);

        Assert.Equal(
            Path.Combine("/custom/state", "HyperionMinecraftLauncher"),
            path);
    }

    [Fact]
    public void Resolve_Linux_WithoutXdgStateHome_FallsBackToLocalState()
    {
        var env = new FakeEnvironment
        {
            CurrentPlatform = OSPlatform.Linux,
        }
        .SetVar("XDG_STATE_HOME", null)
        .SetFolder(Environment.SpecialFolder.UserProfile, "/home/tester");

        var path = DefaultLogDirectory.Resolve(env);

        Assert.Equal(
            Path.Combine("/home/tester", ".local", "state", "HyperionMinecraftLauncher"),
            path);
    }

    [Fact]
    public void Resolve_Linux_WithBlankXdgStateHome_FallsBackToLocalState()
    {
        // POSIX convention: a blank/empty XDG_* variable should be treated as unset.
        var env = new FakeEnvironment
        {
            CurrentPlatform = OSPlatform.Linux,
        }
        .SetVar("XDG_STATE_HOME", "")
        .SetFolder(Environment.SpecialFolder.UserProfile, "/home/tester");

        var path = DefaultLogDirectory.Resolve(env);

        Assert.Equal(
            Path.Combine("/home/tester", ".local", "state", "HyperionMinecraftLauncher"),
            path);
    }

    [Fact]
    public void Resolve_Windows_UsesLocalAppDataLogsSubdirectory()
    {
        var env = new FakeEnvironment
        {
            CurrentPlatform = OSPlatform.Windows,
        }
        .SetFolder(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\tester\AppData\Local");

        var path = DefaultLogDirectory.Resolve(env);

        Assert.Equal(
            Path.Combine(@"C:\Users\tester\AppData\Local", "HyperionMinecraftLauncher", "logs"),
            path);
    }

    [Fact]
    public void Resolve_MacOs_UsesLibraryLogsFolder()
    {
        var env = new FakeEnvironment
        {
            CurrentPlatform = OSPlatform.OSX,
        }
        .SetFolder(Environment.SpecialFolder.UserProfile, "/Users/tester");

        var path = DefaultLogDirectory.Resolve(env);

        Assert.Equal(
            Path.Combine("/Users/tester", "Library", "Logs", "HyperionMinecraftLauncher"),
            path);
    }

    [Fact]
    public void Resolve_NoArguments_FallsBackOnDefaultEnvironment()
    {
        // Sanity: the parameterless overload must still work for legacy callers.
        var path = DefaultLogDirectory.Resolve();
        Assert.False(string.IsNullOrEmpty(path));
    }
}
