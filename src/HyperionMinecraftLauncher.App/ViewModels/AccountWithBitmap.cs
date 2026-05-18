using Avalonia.Media.Imaging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// View-model wrapper around <see cref="Account"/> that carries a pre-resolved head-face
/// <see cref="Bitmap"/> for the account-switcher flyout. The header chip already shows the
/// active session's real skin via <see cref="MainViewModel.AvatarBitmap"/>; this projection
/// gives every row in the flyout the same affordance instead of the bundled Steve face.
/// </summary>
/// <remarks>
/// <para>
/// The bitmap is loaded once per <see cref="MainViewModel.RefreshAccountsAsync"/> call (so
/// re-opening the flyout doesn't refetch) and may be <c>null</c> when the account is offline,
/// when Mojang returned a 404 (account migrated / deleted), or when the network was down.
/// The flyout XAML falls back to the bundled Steve face in those cases.
/// </para>
/// <para>
/// <see cref="Id"/>, <see cref="Username"/>, <see cref="Uuid"/>, and <see cref="IsOffline"/>
/// are passed through so existing XAML bindings (and the <see cref="UuidToShort"/> /
/// <see cref="ActiveAccountMatchMulti"/> converters) keep working without changes.
/// </para>
/// </remarks>
public sealed record AccountWithBitmap
{
    /// <summary>The underlying account this projection wraps.</summary>
    public required Account Account { get; init; }

    /// <summary>
    /// Pre-cropped 8x8 head face from the player's skin, decoded into an Avalonia
    /// <see cref="Bitmap"/>. <c>null</c> when no skin could be fetched - the flyout's
    /// Steve fallback covers that case.
    /// </summary>
    public Bitmap? HeadBitmap { get; init; }

    /// <summary>Pass-through to <see cref="Account.Id"/> for XAML binding convenience.</summary>
    public string Id => Account.Id;

    /// <summary>Pass-through to <see cref="Account.Username"/>.</summary>
    public string Username => Account.Username;

    /// <summary>Pass-through to <see cref="Account.Uuid"/>.</summary>
    public string Uuid => Account.Uuid;

    /// <summary>Pass-through to <see cref="Account.IsOffline"/>.</summary>
    public bool IsOffline => Account.IsOffline;
}
