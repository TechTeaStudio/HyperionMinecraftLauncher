using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

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
}
