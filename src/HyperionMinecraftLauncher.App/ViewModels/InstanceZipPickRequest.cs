using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Delegate the View injects so the view-model can ask the user to pick a destination
/// path for an instance export <c>.zip</c> without taking a direct dependency on Avalonia's
/// file picker. Returns the chosen absolute path, or <c>null</c> if the user cancelled.
/// </summary>
/// <param name="suggestedFileName">Name to seed the picker with (e.g. <c>"MyPack.zip"</c>).</param>
public delegate Task<string?> InstanceExportZipPickRequest(string suggestedFileName, CancellationToken cancellationToken);

/// <summary>
/// Delegate the View injects so the view-model can ask the user to pick an existing
/// <c>.zip</c> file to import. Returns the chosen absolute path, or <c>null</c> if the
/// user cancelled.
/// </summary>
public delegate Task<string?> InstanceImportZipPickRequest(CancellationToken cancellationToken);

/// <summary>A MultiMC / Prism source the user picked and whether to copy it or link to it in place.</summary>
/// <param name="Path">Absolute path to the instance <c>.zip</c> or folder.</param>
/// <param name="Mode">Copy (zip or folder) or link (folder only).</param>
public sealed record MultiMcImportPick(string Path, MultiMcImportMode Mode);

/// <summary>
/// Delegate the View injects so the view-model can ask the user to pick a MultiMC / Prism
/// instance: a <c>.zip</c> or a folder to copy, or a folder to link without copying. The
/// View shows a chooser dialog with three buttons and then the matching picker; returns
/// <c>null</c> when cancelled.
/// </summary>
public delegate Task<MultiMcImportPick?> MultiMcImportPickRequest(CancellationToken cancellationToken);
