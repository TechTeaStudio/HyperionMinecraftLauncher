using System.Threading;
using System.Threading.Tasks;

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

/// <summary>
/// Delegate the View injects so the view-model can ask the user to pick either a
/// MultiMC / Prism instance <c>.zip</c> file OR an unzipped instance folder. The View
/// shows whatever flow is most natural for the host platform (a chooser dialog with two
/// buttons; the picked path is the return value, or <c>null</c> when cancelled).
/// </summary>
public delegate Task<string?> MultiMcImportPickRequest(CancellationToken cancellationToken);
