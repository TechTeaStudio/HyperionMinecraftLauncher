namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One zipped shader pack under <c>&lt;gameDir&gt;/shaderpacks/</c>. Same shape as
/// <see cref="ResourcePackEntry"/>: Iris / OptiFine treat <c>.zip.disabled</c> as a skip
/// marker, so toggling enable/disable is a rename rather than a delete.
/// </summary>
public sealed record ShaderPackEntry
{
    /// <summary>Absolute path to the pack file on disk.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without directory (e.g. <c>"BSL_v8.zip"</c>).</summary>
    public required string Filename { get; init; }

    /// <summary>File length in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>True when the file ends in <c>.zip</c>; false when the file ends in <c>.zip.disabled</c>.</summary>
    public required bool IsEnabled { get; init; }
}
