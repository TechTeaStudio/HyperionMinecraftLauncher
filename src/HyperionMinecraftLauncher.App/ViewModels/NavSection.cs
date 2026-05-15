namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

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

    /// <summary>Minecraft.net news feed + per-version patch notes. Planned for a later release.</summary>
    News,

    /// <summary>Memory slider, JVM args, game directory, launcher prefs. Planned for a later release.</summary>
    Settings,
}
