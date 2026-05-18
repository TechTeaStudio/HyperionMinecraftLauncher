using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Test seam over the slice of <c>CmlLib.Core.MinecraftLauncher</c> we actually use.
/// Production code wires this to a thin CmlLib adapter; tests substitute a fake so
/// they never need a real Minecraft installation, network, or Java.
/// </summary>
public interface IUnderlyingLauncher
{
    /// <summary>Fetch the version manifest (release + snapshot list).</summary>
    Task<IReadOnlyList<VersionMetadata>> GetAllVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Install (download + extract) the named version, surfacing progress through the provided <see cref="IProgress{T}"/>.</summary>
    Task InstallAsync(
        string versionName,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Spawn the Java process for the given <paramref name="request"/>, returning its OS process id.
    /// All session, RAM, and Quick Play settings are read from the request - keeps this interface
    /// resilient to future additions on <see cref="LaunchRequest"/> without further sig churn.
    /// </summary>
    Task<int> StartProcessAsync(
        LaunchRequest request,
        CancellationToken cancellationToken);
}
