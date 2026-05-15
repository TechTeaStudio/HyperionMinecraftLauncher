namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>Outcome of <see cref="IMinecraftLauncherService.LaunchAsync"/>.</summary>
public sealed record LaunchResult
{
    /// <summary>Operating-system process id of the spawned Java process.</summary>
    public required int ProcessId { get; init; }

    /// <summary>The version id that was actually launched (matches <see cref="LaunchRequest.VersionName"/>).</summary>
    public required string VersionName { get; init; }
}
