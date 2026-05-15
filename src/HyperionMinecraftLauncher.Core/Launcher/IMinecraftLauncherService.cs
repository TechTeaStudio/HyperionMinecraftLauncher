using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Abstract Minecraft launcher contract. The Core library ships one implementation
/// (<see cref="CmlLibMinecraftLauncherService"/>) wrapping the CmlLib.Core library;
/// tests substitute fakes through this interface.
/// </summary>
public interface IMinecraftLauncherService
{
    /// <summary>
    /// Fetch the list of installable Minecraft versions (release + snapshot, sorted newest-first).
    /// </summary>
    Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enumerate versions already present on disk under the resolved <c>.minecraft</c> directory.
    /// Returns an empty list when the directory is missing - useful so the UI can render a "no installs yet" state.
    /// </summary>
    Task<IReadOnlyList<InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolve a session from the supplied <paramref name="request"/>.
    /// Offline / Microsoft / Mojang paths are dispatched on <see cref="AuthRequest.Mode"/>.
    /// </summary>
    Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Install (if missing) and launch the requested Minecraft version.
    /// The returned <see cref="LaunchResult"/> reports the spawned process id and exit semantics.
    /// </summary>
    Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken);
}
