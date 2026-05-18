namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Sub-tabs on the Installations page detail panel - shown to the right of the
/// instance grid once an instance is selected. Three views over the per-instance
/// gameDir: screenshots, world saves, and the multiplayer server list.
/// </summary>
public enum InstanceDetailTab
{
    Screenshots,
    Worlds,
    Servers,
    /// <summary>Recent crash reports parsed from <c>&lt;gameDir&gt;/crash-reports/</c>.</summary>
    Crashes,
}

/// <summary>Identifies the active sidebar item. The content area binds <c>IsVisible</c> to a converter that matches this enum.</summary>
public enum NavSection
{
    /// <summary>Default landing - account chip, version picker, Launch button, log.</summary>
    Home,

    /// <summary>List + CRUD of installed Minecraft versions (vanilla / Forge / Fabric / Quilt). Planned for a later release.</summary>
    Installations,

    /// <summary>Skin + cape preview and upload. Planned for a later release.</summary>
    Skins,

    /// <summary>Server list editor (<c>servers.dat</c>). Planned for a later release.</summary>
    Servers,

    /// <summary>Headless dedicated server manager. Creates Hyperion-tracked server folders;
    /// the server-jar download + process spawn is wired in v0.29.</summary>
    HeadlessServers,

    /// <summary>Modrinth + CurseForge mod browser, per-instance install / enable / remove.</summary>
    Mods,

    /// <summary>Minecraft.net news feed + per-version patch notes. Planned for a later release.</summary>
    News,

    /// <summary>Memory slider, JVM args, game directory, launcher prefs. Planned for a later release.</summary>
    Settings,

    /// <summary>Today's daily-rotated launcher log file with filter, refresh, open-folder and copy-all actions.</summary>
    Logs,
}
