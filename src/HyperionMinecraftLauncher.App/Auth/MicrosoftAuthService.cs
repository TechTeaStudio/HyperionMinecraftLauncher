using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Auth;

/// <summary>
/// Production <see cref="IMicrosoftAuthService"/>. Backed by <c>CmlLib.Core.Auth.Microsoft.JELoginHandler</c>
/// with the OAuth provider swapped to <c>MsalDeviceCodeProvider</c>:
///
///   * The default XboxAuthNet OAuth flow tries to host a WebView2 instance, which has no
///     binding into Avalonia and crashes with <c>"Current platform does not support to
///     provide default WebUI."</c> on first sign-in.
///   * The MSAL device-code flow needs zero platform integration: Microsoft hands us a
///     short user-code, we show it to the user (event + log) and open the verification URL
///     in their default browser. MSAL polls in the background until they enter the code.
///
/// MSAL's own token cache lives in <c>%LOCALAPPDATA%\.IdentityService\</c>, so silent sign-in
/// on subsequent launches uses the cached refresh token and skips the device-code prompt.
/// </summary>
public sealed class MicrosoftAuthService : IMicrosoftAuthService
{
    /// <summary>The Mojang/Minecraft MSAL client_id - the same GUID every OSS launcher uses.</summary>
    private const string MinecraftMsalClientId = "499c8d36-be2a-4231-9ebd-ef291b7bb64c";

    private readonly JELoginHandler _handler;
    private readonly ILauncherLogger _logger;

    /// <inheritdoc />
    public event EventHandler<MicrosoftDeviceCodeInfo>? DeviceCodeRequested;

    /// <summary>Default constructor: account cache under <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json</c>.</summary>
    public MicrosoftAuthService(ILauncherLogger logger) : this(logger, DefaultAccountCachePath()) { }

    /// <summary>Test / advanced constructor with an explicit cache path.</summary>
    public MicrosoftAuthService(ILauncherLogger logger, string accountCachePath)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountCachePath);

        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(accountCachePath)!);

        // Build an MSAL public-client app with its own on-disk token cache. The Build*WithCache
        // helper is async; we block briefly on construction because the cache load is tiny.
        var msalApp = MsalClientHelper.BuildApplicationWithCache(MinecraftMsalClientId)
            .GetAwaiter().GetResult();

        var deviceCodeProvider = new MsalDeviceCodeProvider(msalApp, deviceCode =>
        {
            var info = new MicrosoftDeviceCodeInfo
            {
                UserCode = deviceCode.UserCode ?? string.Empty,
                VerificationUrl = deviceCode.VerificationUrl ?? "https://microsoft.com/devicelogin",
                Message = deviceCode.Message ?? string.Empty,
            };
            _logger.Info($"Microsoft device-code: {info.UserCode} @ {info.VerificationUrl}");

            try
            {
                DeviceCodeRequested?.Invoke(this, info);
            }
            catch (Exception ex)
            {
                _logger.Warn($"DeviceCodeRequested subscriber threw: {ex.Message}");
            }

            // Open the verification page in the default browser. Best-effort; missing browser is fine
            // - the user can also type the URL themselves from the log message.
            try
            {
                Process.Start(new ProcessStartInfo(info.VerificationUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Could not auto-open browser to {info.VerificationUrl}: {ex.Message}");
            }

            return Task.CompletedTask;
        });

        _handler = new JELoginHandlerBuilder()
            .WithAccountManager(accountCachePath)
            .WithOAuthProvider(deviceCodeProvider)
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
        _logger.Info("Microsoft sign-in: silent-then-device-code (MSAL).");
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
        _logger.Info("Microsoft sign-in: forced device-code (MSAL).");
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
