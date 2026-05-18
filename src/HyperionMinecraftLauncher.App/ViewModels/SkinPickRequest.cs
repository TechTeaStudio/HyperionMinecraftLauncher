using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// What the View hands back to <see cref="MainViewModel.UploadSkinCommand"/> after the
/// user picks a PNG and chooses Classic vs Slim. <c>null</c> means the user cancelled.
/// </summary>
public sealed record SkinPickResult(byte[] PngBytes, SkinVariant Variant);

/// <summary>
/// Delegate the View injects into <see cref="MainViewModel"/> so the view-model can ask
/// for "a PNG + a variant" without taking a direct dependency on Avalonia's file picker.
/// </summary>
public delegate Task<SkinPickResult?> SkinPickRequest(CancellationToken cancellationToken);
