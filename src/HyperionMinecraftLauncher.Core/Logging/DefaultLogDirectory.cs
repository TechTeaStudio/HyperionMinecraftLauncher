using System;
using System.IO;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Resolves the per-platform default location for HyperionMinecraftLauncher's log files.
///
/// Windows:        <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/logs</c>
/// Linux:          <c>~/.local/share/HyperionMinecraftLauncher/logs</c>
/// macOS:          <c>~/Library/Application Support/HyperionMinecraftLauncher/logs</c>
///
/// All resolved through <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
/// </summary>
public static class DefaultLogDirectory
{
    /// <summary>The product-level folder name that the launcher claims under <c>LocalAppData</c>.</summary>
    public const string ApplicationName = "HyperionMinecraftLauncher";

    /// <summary>The leaf directory name (under <see cref="ApplicationName"/>).</summary>
    public const string LogsSubdirectory = "logs";

    /// <summary>
    /// Returns the absolute path of the platform's preferred log directory.
    /// The directory itself is *not* created by this call - the file logger handles that lazily.
    /// </summary>
    public static string Resolve()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            // Some headless / sandboxed environments return empty here; fall back to a temp path so
            // the launcher can still record errors instead of crashing on startup.
            root = Path.Combine(Path.GetTempPath(), "HyperionMinecraftLauncher-data");
        }

        return Path.Combine(root, ApplicationName, LogsSubdirectory);
    }
}
