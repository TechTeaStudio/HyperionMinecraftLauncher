namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// The JRE feature versions Minecraft has required across its history.
/// Mojang bumps the minimum JRE every few major versions and the launcher follows.
/// </summary>
public enum JavaRequirement
{
    /// <summary>Required by Minecraft 1.16.x and earlier (1.8 .. 1.16.5).</summary>
    Java8,

    /// <summary>Required by Minecraft 1.17 through 1.20.4.</summary>
    Java17,

    /// <summary>Required by Minecraft 1.20.5 through 1.21.11.</summary>
    Java21,

    /// <summary>Required by the year-based versions, 26.1 and later.</summary>
    Java25,
}
