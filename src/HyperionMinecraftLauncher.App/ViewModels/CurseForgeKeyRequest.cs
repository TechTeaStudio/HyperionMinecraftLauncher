using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Delegate the View injects into <see cref="MainViewModel"/> so the view-model can ask
/// for "a CurseForge API key, please" without taking a direct dependency on Avalonia's
/// dialog system. Same shape as <see cref="SkinPickRequest"/>.
///
/// The <paramref name="existingKey"/> parameter is the key currently stored in settings
/// (empty when the user has never configured one); the dialog uses it to pre-populate the
/// textbox so "Change key" shows the user what's there before they paste a fresh value.
///
/// Returns the trimmed non-empty key the user confirmed, or <c>null</c> when they cancelled.
/// </summary>
public delegate Task<string?> CurseForgeKeyRequest(string existingKey, CancellationToken cancellationToken);
