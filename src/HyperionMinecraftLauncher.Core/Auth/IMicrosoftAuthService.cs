using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>
/// Microsoft account sign-in for Minecraft: Java Edition. Hides the CmlLib /
/// XboxAuthNet stack so the Core launcher service can speak in neutral DTOs
/// (<see cref="AuthResult"/>) and tests can swap in a fake without pulling in WebView2.
/// </summary>
/// <remarks>
/// The contract is intentionally three methods + one property:
/// silent-then-interactive, force-interactive, signout, cache-hint. That maps
/// 1-to-1 onto <c>CmlLib.Core.Auth.Microsoft.JELoginHandler</c>'s shipped surface.
/// </remarks>
public interface IMicrosoftAuthService
{
    /// <summary>True when the local cache already holds a Microsoft refresh token,
    /// so <see cref="SignInAsync"/> can complete silently without showing a browser.</summary>
    bool HasCachedAccount { get; }

    /// <summary>Try the cached refresh token first; fall back to interactive sign-in
    /// (system browser / WebView2) if the cache is empty or expired.</summary>
    Task<AuthResult> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Skip the cache and force an interactive sign-in.</summary>
    Task<AuthResult> SignInInteractiveAsync(CancellationToken cancellationToken);

    /// <summary>Clear the local account cache.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}
