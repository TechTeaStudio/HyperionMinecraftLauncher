using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

/// <summary>How an imported MultiMC / Prism instance gets its game directory.</summary>
public enum MultiMcImportMode
{
    /// <summary>Copy the <c>.minecraft</c> tree into a fresh folder under Hyperion's data root.</summary>
    Copy,

    /// <summary>
    /// Copy nothing: the instance plays straight from the MultiMC folder's <c>.minecraft</c>,
    /// so worlds, mods and settings stay shared with MultiMC. Folder sources only.
    /// </summary>
    Link,
}

/// <summary>What an import produced: the instance to persist and the time played in the source launcher.</summary>
public sealed record MultiMcImportResult
{
    /// <summary>The built instance; the caller persists it through <see cref="IInstanceStore"/>.</summary>
    public required Instance Instance { get; init; }

    /// <summary>MultiMC / Prism's <c>totalTimePlayed</c>; zero when the cfg does not say.</summary>
    public TimeSpan PlayedInSource { get; init; }
}

/// <summary>
/// Imports a MultiMC / Prism Launcher instance into Hyperion as a first-class
/// <see cref="Instance"/>. Accepts either a shared <c>.zip</c> (the format Prism's
/// "Export instance" produces) or the on-disk instance folder. Throws an
/// <see cref="Export.InstanceImportException"/> with a friendly message when the source
/// is missing <c>instance.cfg</c> / <c>mmc-pack.json</c>, the files are malformed, or a
/// zip is asked to be linked.
/// </summary>
public interface IMultiMcInstanceImporter
{
    /// <summary>
    /// Open <paramref name="sourceZipOrFolderPath"/>, pull the Prism settings + component list
    /// out and build the <see cref="Instance"/>. <see cref="MultiMcImportMode.Copy"/> copies the
    /// <c>.minecraft</c> tree into a fresh per-instance folder; <see cref="MultiMcImportMode.Link"/>
    /// points the instance at the source's own <c>.minecraft</c> and copies nothing.
    /// </summary>
    /// <param name="sourceZipOrFolderPath">Absolute path to the Prism <c>.zip</c> or the unzipped instance folder.</param>
    /// <param name="mode">Copy the game directory or link to it in place.</param>
    /// <param name="overrideName">Optional Hyperion-side name. Null = keep the cfg's <c>name=</c>.</param>
    /// <param name="progress">0..1 progress reporter (copied bytes vs. total; a link reports 1.0 at once).</param>
    /// <param name="cancellationToken">Standard cancellation; partial copies may be left behind.</param>
    Task<MultiMcImportResult> ImportAsync(
        string sourceZipOrFolderPath,
        MultiMcImportMode mode,
        string? overrideName,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
