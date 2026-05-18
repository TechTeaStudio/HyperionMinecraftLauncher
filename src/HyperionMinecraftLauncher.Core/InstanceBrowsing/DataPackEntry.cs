namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One zipped data pack under <c>&lt;gameDir&gt;/saves/&lt;world&gt;/datapacks/</c>. Unlike
/// resource and shader packs, data packs live per-world. The UI groups entries by
/// <see cref="WorldFolderName"/> so the user can tell which save a pack belongs to.
/// </summary>
public sealed record DataPackEntry
{
    /// <summary>Absolute path to the pack file on disk.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without directory (e.g. <c>"vanilla-tweaks.zip"</c>).</summary>
    public required string Filename { get; init; }

    /// <summary>File length in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Folder name of the world this pack belongs to (the parent's parent directory of the file).</summary>
    public required string WorldFolderName { get; init; }

    /// <summary>True when the file ends in <c>.zip</c>; false when the file ends in <c>.zip.disabled</c>.</summary>
    public required bool IsEnabled { get; init; }
}
