using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// CmlLib-backed implementation of <see cref="IMinecraftLauncherService"/>.
/// Owns no CmlLib types directly - all interaction is funnelled through
/// <see cref="IUnderlyingLauncher"/>, which keeps tests synchronous and offline.
/// </summary>
public sealed class CmlLibMinecraftLauncherService : IMinecraftLauncherService
{
    private readonly IUnderlyingLauncher _underlying;
    private readonly ILauncherLogger _logger;
    private readonly IMicrosoftAuthService? _microsoftAuth;
    private readonly IInstalledVersionScanner _installedScanner;
    private readonly IMinecraftInstallationLocator _installationLocator;
    private readonly ILauncherProfilesStore _profilesStore;
    private readonly IServersStore _serversStore;
    private readonly INewsClient _newsClient;
    private readonly IInstanceStore _instanceStore;

    /// <summary>Primary constructor used by the App and by tests.</summary>
    /// <param name="microsoftAuth">Optional Microsoft sign-in provider. When omitted, <see cref="AuthMode.Microsoft"/>
    /// requests throw with a friendly "not configured" message - useful in headless / test contexts that don't
    /// link the WebView2-dependent <c>MicrosoftAuthService</c>.</param>
    /// <param name="installedScanner">Reads <c>versions/&lt;id&gt;/&lt;id&gt;.json</c> from disk. Defaults to a fresh
    /// <see cref="FileSystemInstalledVersionScanner"/> if omitted.</param>
    /// <param name="installationLocator">Locates the platform-default <c>.minecraft</c> directory. Defaults to
    /// <see cref="DefaultMinecraftInstallationLocator"/>.</param>
    public CmlLibMinecraftLauncherService(
        IUnderlyingLauncher underlying,
        ILauncherLogger logger,
        IMicrosoftAuthService? microsoftAuth = null,
        IInstalledVersionScanner? installedScanner = null,
        IMinecraftInstallationLocator? installationLocator = null,
        ILauncherProfilesStore? profilesStore = null,
        IServersStore? serversStore = null,
        INewsClient? newsClient = null,
        IInstanceStore? instanceStore = null)
    {
        _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _microsoftAuth = microsoftAuth;
        _installedScanner = installedScanner ?? new FileSystemInstalledVersionScanner();
        _installationLocator = installationLocator ?? new DefaultMinecraftInstallationLocator();
        _profilesStore = profilesStore ?? new FileLauncherProfilesStore();
        _serversStore = serversStore ?? new FileServersStore();
        _newsClient = newsClient ?? new MojangNewsClient();
        _instanceStore = instanceStore ?? new FileInstanceStore();
    }

    /// <summary>
    /// Convenience factory: wire a real <see cref="CmlLibUnderlyingLauncher"/> with the platform-default
    /// <c>.minecraft</c> directory. Used by the App's manual DI in <c>App.axaml.cs</c>.
    /// </summary>
    public static CmlLibMinecraftLauncherService Create(ILauncherLogger logger, IMicrosoftAuthService? microsoftAuth = null)
        => new(new CmlLibUnderlyingLauncher(), logger, microsoftAuth);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> ListNewsAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Fetching Mojang launcher news feed.");
        var news = await _newsClient.FetchAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {news.Count} news entries.");
        return news;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instance>> ListInstancesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Loading Hyperion instances ...");
        var instances = await _instanceStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {instances.Count} instances.");
        return instances;
    }

    /// <inheritdoc />
    public async Task SaveInstanceAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _logger.Info($"Saving instance '{instance.Name}' (id={instance.Id}, version={instance.VersionId}).");
        await _instanceStore.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteInstanceAsync(string id, CancellationToken cancellationToken)
    {
        _logger.Info($"Deleting instance id={id}.");
        await _instanceStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerListEntry>> ListServersAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Reading servers.dat from '{install.ServersDatPath}'.");
        var servers = await _serversStore.LoadAsync(install.ServersDatPath, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {servers.Count} servers.");
        return servers;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Reading launcher_profiles.json from '{install.LauncherProfilesPath}'.");
        try
        {
            var file = await _profilesStore.LoadAsync(install.LauncherProfilesPath, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Loaded {file.Profiles.Count} profiles (schema v{file.SchemaVersion}).");
            return file.Profiles;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // launcher_profiles.json can be malformed if the user's been editing it by hand;
            // surface that as an empty list rather than a hard error.
            _logger.Warn($"Could not parse launcher_profiles.json: {ex.Message}");
            return Array.Empty<LauncherProfile>();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Scanning installed versions under '{install.VersionsDirectory}'.");
        // The scan is a few file reads of small JSON manifests; wrap in Task.Run so the UI thread
        // never blocks on disk IO even though IInstalledVersionScanner is synchronous.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<InstalledVersion> list = _installedScanner.Scan(install.VersionsDirectory);
            _logger.Info($"Found {list.Count} installed versions.");
            return list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Listing Minecraft versions ...");
        try
        {
            var list = await _underlying.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"Loaded {list.Count} versions from the manifest.");
            return list;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            var wrapped = new InstallationFailedException("Could not fetch the Minecraft version manifest (network error).", ex);
            _logger.Error("ListVersionsAsync failed (network).", wrapped);
            throw wrapped;
        }
        catch (Exception ex)
        {
            var wrapped = new InstallationFailedException("Could not fetch the Minecraft version manifest.", ex);
            _logger.Error("ListVersionsAsync failed.", wrapped);
            throw wrapped;
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // For Microsoft mode the username comes from the OAuth-issued profile, not the request.
        // We only require a username for offline play.
        if (request.Mode == AuthMode.Offline && string.IsNullOrWhiteSpace(request.Username))
        {
            var ex = new AuthenticationFailedException("Username is required for offline authentication.");
            _logger.Error("AuthenticateAsync rejected: empty username (offline).", ex);
            throw ex;
        }

        switch (request.Mode)
        {
            case AuthMode.Offline:
                var session = MSession.CreateOfflineSession(request.Username);
                _logger.Info($"Offline session created for '{request.Username}'.");
                return new AuthResult
                {
                    Username = session.Username ?? request.Username,
                    Uuid = session.UUID ?? string.Empty,
                    AccessToken = session.AccessToken ?? string.Empty,
                    IsOffline = true,
                };

            case AuthMode.Microsoft:
            {
                if (_microsoftAuth is null)
                {
                    var ex = new AuthenticationFailedException(
                        "Microsoft sign-in is not configured: no IMicrosoftAuthService was injected. " +
                        "The desktop App wires this up via MicrosoftAuthService; headless / test contexts must " +
                        "supply their own implementation or use AuthMode.Offline.");
                    _logger.Error("AuthenticateAsync rejected: Microsoft auth not configured.", ex);
                    throw ex;
                }

                _logger.Info("Microsoft sign-in: delegating to IMicrosoftAuthService.");
                try
                {
                    var result = await _microsoftAuth.SignInAsync(cancellationToken).ConfigureAwait(false);
                    _logger.Info($"Microsoft sign-in succeeded for '{result.Username}'.");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (AuthenticationFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var wrapped = new AuthenticationFailedException($"Microsoft sign-in failed: {ex.Message}", ex);
                    _logger.Error("Microsoft sign-in failed.", wrapped);
                    throw wrapped;
                }
            }

            case AuthMode.Mojang:
            {
                var ex = new AuthenticationFailedException(
                    "Mojang username+password authentication was discontinued by Mojang in 2022 " +
                    "and is not supported by this launcher. Use AuthMode.Microsoft or AuthMode.Offline.");
                _logger.Error("AuthenticateAsync rejected: Mojang auth removed upstream.", ex);
                throw ex;
            }

            default:
            {
                var ex = new AuthenticationFailedException($"Unknown AuthMode '{request.Mode}'.");
                _logger.Error("AuthenticateAsync rejected: unknown mode.", ex);
                throw ex;
            }
        }
    }

    /// <inheritdoc />
    public async Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Session);

        if (string.IsNullOrWhiteSpace(request.VersionName))
        {
            var ex = new VersionNotFoundException(request.VersionName ?? string.Empty);
            _logger.Error("LaunchAsync rejected: empty version name.", ex);
            throw ex;
        }

        _logger.Info($"Installing Minecraft '{request.VersionName}' for user '{request.Session.Username}' ...");

        try
        {
            await _underlying.InstallAsync(request.VersionName, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException ex)
        {
            var wrapped = new VersionNotFoundException(request.VersionName, ex);
            _logger.Error($"Version '{request.VersionName}' not found during install.", wrapped);
            throw wrapped;
        }
        catch (HttpRequestException ex)
        {
            var wrapped = new InstallationFailedException(
                $"Network error while installing '{request.VersionName}'. Check your connection and retry.", ex);
            _logger.Error($"Install failed (network) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (IOException ex)
        {
            var wrapped = new InstallationFailedException(
                $"I/O error while installing '{request.VersionName}'. The destination directory may be locked or full.", ex);
            _logger.Error($"Install failed (I/O) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (UnauthorizedAccessException ex)
        {
            var wrapped = new InstallationFailedException(
                $"Permission denied while installing '{request.VersionName}'. Try running as a normal user or change the game directory.", ex);
            _logger.Error($"Install failed (permissions) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (LauncherException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = new InstallationFailedException(
                $"Unexpected error while installing '{request.VersionName}': {ex.Message}", ex);
            _logger.Error($"Install failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }

        progress?.Report(new LaunchProgress { Stage = "Starting game" });
        _logger.Info($"Starting Minecraft '{request.VersionName}' ...");

        try
        {
            var pid = await _underlying.StartProcessAsync(
                request.VersionName,
                request.Session.Username,
                request.Session.Uuid,
                request.Session.AccessToken,
                request.MinimumRamMb,
                request.MaximumRamMb,
                cancellationToken).ConfigureAwait(false);

            _logger.Info($"Minecraft '{request.VersionName}' started (pid {pid}).");
            return new LaunchResult { ProcessId = pid, VersionName = request.VersionName };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            var wrapped = new GameProcessStartException(
                $"Could not start the Minecraft Java process. Is Java installed and on PATH? Underlying message: {ex.Message}", ex);
            _logger.Error($"Process start failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (FileNotFoundException ex)
        {
            var wrapped = new GameProcessStartException(
                $"Could not start Minecraft - a required file is missing: {ex.FileName ?? ex.Message}", ex);
            _logger.Error($"Process start failed (file missing) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (LauncherException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = new GameProcessStartException(
                $"Unexpected error starting Minecraft '{request.VersionName}': {ex.Message}", ex);
            _logger.Error($"Process start failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
    }
}
