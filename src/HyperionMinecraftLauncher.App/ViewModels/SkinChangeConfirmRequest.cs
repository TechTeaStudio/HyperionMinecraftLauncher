using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// What the player is asked to confirm before a skin goes to Mojang: the new skin, its model
/// and the account whose skin it replaces.
/// </summary>
public sealed record SkinChangeConfirmation(byte[] NewPngBytes, SkinVariant Variant, string AccountName);

/// <summary>
/// Delegate the View injects into <see cref="MainViewModel"/> to show the "change skin?" dialog.
/// Returns <c>true</c> only when the player confirmed the change.
/// </summary>
public delegate Task<bool> SkinChangeConfirmRequest(SkinChangeConfirmation request, CancellationToken cancellationToken);
