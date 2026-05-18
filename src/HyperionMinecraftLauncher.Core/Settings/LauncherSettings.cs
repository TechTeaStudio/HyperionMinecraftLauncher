namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

/// <summary>
/// Persisted Hyperion launcher preferences. Lives at
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\settings.json</c> on Windows
/// (next to the logs directory). Defaults are tuned for a 16 GB Windows desktop;
/// the slider in the Settings page clamps to a sane fraction of the detected RAM.
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>JVM minimum heap in MiB. Default 1024 (1 GB).</summary>
    public int MinimumRamMb { get; init; } = 1024;

    /// <summary>JVM maximum heap in MiB. Default 4096 (4 GB).</summary>
    public int MaximumRamMb { get; init; } = 4096;

    /// <summary>Extra JVM args appended after the heap flags. Default is empty so CmlLib's own args apply.</summary>
    public string JvmArguments { get; init; } = string.Empty;

    /// <summary>Override the <c>.minecraft</c> root. <c>null</c> = OS default.</summary>
    public string? GameDirectory { get; init; }

    /// <summary>Override the Java executable. <c>null</c> = CmlLib's auto-detect / bundled runtime.</summary>
    public string? JavaExecutable { get; init; }

    /// <summary>If true, the launcher window stays open after the Minecraft process starts.</summary>
    public bool KeepLauncherOpen { get; init; } = true;

    /// <summary>If true, the in-app log mirrors the game's stdout/stderr (planned hook - not wired yet).</summary>
    public bool ShowGameLog { get; init; }

    /// <summary>
    /// CurseForge API key. Default is empty - CurseForge requires per-developer keys and we
    /// don't ship one. When empty the launcher silently disables CurseForge search and any
    /// attempt to download a CurseForge file throws a "key not configured" error. Users who
    /// register their own free key at <c>https://console.curseforge.com</c> can paste it into
    /// Settings to light up the integration.
    /// </summary>
    public string CurseForgeApiKey { get; init; } = string.Empty;
}
