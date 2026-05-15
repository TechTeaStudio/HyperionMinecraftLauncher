using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Auth;

/// <summary>
/// Production <see cref="IMicrosoftAuthService"/> backed by
/// <c>CmlLib.Core.Auth.Microsoft.JELoginHandler</c>. Lives in the Windows-targeting
/// App project because <c>JELoginHandler</c>'s default OAuth path uses WebView2;
/// the Core library stays portable and only sees the <see cref="IMicrosoftAuthService"/>
/// interface.
/// </summary>
public sealed class MicrosoftAuthService : IMicrosoftAuthService
{
    private readonly JELoginHandler _handler;
    private readonly ILauncherLogger _logger;

    /// <summary>
    /// Build a handler that stores its account cache next to the launcher's logs
    /// (e.g. <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json</c>), so we
    /// never touch the official launcher's <c>launcher_accounts.json</c>.
    /// </summary>
    public MicrosoftAuthService(ILauncherLogger logger)
        : this(logger, DefaultAccountCachePath())
    {
    }

    /// <summary>Test / advanced constructor with an explicit cache path.</summary>
    public MicrosoftAuthService(ILauncherLogger logger, string accountCachePath)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountCachePath);

        _logger = logger;

        Directory.CreateDirectory(Path.GetDirectoryName(accountCachePath)!);

        _handler = new JELoginHandlerBuilder()
            .WithAccountManager(accountCachePath)
            .Build();
    }

    /// <inheritdoc />
    public bool HasCachedAccount
    {
        get
        {
            try
            {
                return _handler.AccountManager.GetAccounts().Count > 0;
            }
            catch (Exception ex)
            {
                _logger.Warn($"HasCachedAccount probe failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-in: silent-then-interactive.");
        try
        {
            var session = await _handler.Authenticate(cancellationToken).ConfigureAwait(false);
            return ToAuthResult(session);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Microsoft sign-in cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            throw TranslateAndLog(ex, interactive: false);
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInInteractiveAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-in: forced interactive.");
        try
        {
            var session = await _handler.AuthenticateInteractively(cancellationToken).ConfigureAwait(false);
            return ToAuthResult(session);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Microsoft interactive sign-in cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            throw TranslateAndLog(ex, interactive: true);
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-out.");
        try
        {
            await _handler.Signout(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Microsoft sign-out failed (cache will be force-cleared): {ex.Message}");
            // Best-effort: even if upstream sign-out fails, callers expect the cache cleared.
        }
    }

    private static AuthResult ToAuthResult(MSession session) => new()
    {
        Username = session.Username ?? string.Empty,
        Uuid = session.UUID ?? string.Empty,
        AccessToken = session.AccessToken ?? string.Empty,
        IsOffline = false,
    };

    private AuthenticationFailedException TranslateAndLog(Exception ex, bool interactive)
    {
        // We intentionally don't dispatch on XboxAuthException / MinecraftAuthException
        // by exact type — the package surfaces both as plain `Exception` subclasses with
        // domain-specific Message text, and pinning to concrete types would make us
        // brittle to upstream renames. Surface the message verbatim; callers see the why.
        var prefix = interactive ? "Microsoft interactive sign-in failed" : "Microsoft sign-in failed";
        var wrapped = new AuthenticationFailedException($"{prefix}: {ex.Message}", ex);
        _logger.Error(prefix, wrapped);
        return wrapped;
    }

    private static string DefaultAccountCachePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperionMinecraftLauncher");
        return Path.Combine(dir, "accounts.json");
    }
}
