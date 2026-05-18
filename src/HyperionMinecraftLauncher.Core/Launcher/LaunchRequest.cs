using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Everything the launcher needs to install + start a chosen Minecraft version.
/// </summary>
public sealed record LaunchRequest
{
    /// <summary>The version id (e.g. <c>"1.21.5"</c>).</summary>
    public required string VersionName { get; init; }

    /// <summary>Authenticated session.</summary>
    public required AuthResult Session { get; init; }

    /// <summary>
    /// Override the <c>.minecraft</c> root. <c>null</c> picks the OS default
    /// (<c>%APPDATA%\.minecraft</c> on Windows, <c>~/.minecraft</c> on Linux, etc.).
    /// </summary>
    public string? GameDirectory { get; init; }

    /// <summary>JVM minimum heap in MiB. <c>null</c> leaves it to CmlLib's default.</summary>
    public int? MinimumRamMb { get; init; }

    /// <summary>JVM maximum heap in MiB. <c>null</c> leaves it to CmlLib's default.</summary>
    public int? MaximumRamMb { get; init; }

    /// <summary>
    /// Quick Play target. Defaults to <see cref="QuickPlay.None"/>, which means a regular
    /// launch into the main menu. Set to <see cref="QuickPlay.Singleplayer"/> or
    /// <see cref="QuickPlay.Multiplayer"/> to deep-link straight into a world or server.
    /// </summary>
    public QuickPlay QuickPlay { get; init; } = new QuickPlay.None();

    /// <summary>
    /// Which Java feature version this Minecraft build needs. <c>null</c> means "no managed
    /// JRE - use whatever's on PATH". The view-model populates this via
    /// <see cref="JavaRequirementResolver.For"/> when the user hasn't supplied a manual override.
    /// </summary>
    public JavaRequirement? JavaRequirement { get; init; }

    /// <summary>
    /// Pre-resolved absolute path to the <c>java(.exe)</c> the underlying launcher should use.
    /// Set by the service after <see cref="IJavaRuntimeManager.EnsureRuntimeAsync"/> returns.
    /// <c>null</c> means "let CmlLib decide".
    /// </summary>
    public string? JavaPath { get; init; }
}
