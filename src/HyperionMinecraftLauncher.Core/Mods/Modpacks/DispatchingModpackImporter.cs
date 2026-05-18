using System;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Format-agnostic facade: wraps a Modrinth importer and a CurseForge importer and
/// dispatches to whichever one matches the archive contents. App-layer DI binds
/// <see cref="IModpackImporter"/> to this so the view-model can just call
/// <see cref="ImportAsync"/> without caring which file format the user picked.
/// </summary>
public sealed class DispatchingModpackImporter : IModpackImporter
{
    private readonly IModpackImporter _modrinth;
    private readonly IModpackImporter _curseForge;

    /// <summary>Construct with the per-format importers. Both must be non-null.</summary>
    public DispatchingModpackImporter(IModpackImporter modrinth, IModpackImporter curseForge)
    {
        _modrinth = modrinth ?? throw new ArgumentNullException(nameof(modrinth));
        _curseForge = curseForge ?? throw new ArgumentNullException(nameof(curseForge));
    }

    /// <inheritdoc />
    public Task<Instance> ImportAsync(
        string archivePath,
        string? targetInstanceName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var format = ModpackFormatDetector.Detect(archivePath);
        return format switch
        {
            ModpackFormat.Modrinth => _modrinth.ImportAsync(archivePath, targetInstanceName, progress, cancellationToken),
            ModpackFormat.CurseForge => _curseForge.ImportAsync(archivePath, targetInstanceName, progress, cancellationToken),
            _ => throw new NotSupportedException(
                $"Archive '{System.IO.Path.GetFileName(archivePath)}' is not a recognised modpack " +
                "(needs a top-level 'modrinth.index.json' for .mrpack or 'manifest.json' for CurseForge)."),
        };
    }
}
