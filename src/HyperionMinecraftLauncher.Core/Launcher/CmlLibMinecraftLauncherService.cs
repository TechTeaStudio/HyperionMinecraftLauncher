using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
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

    /// <summary>Primary constructor used by the App and by tests.</summary>
    public CmlLibMinecraftLauncherService(IUnderlyingLauncher underlying, ILauncherLogger logger)
    {
        _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Convenience factory: wire a real <see cref="CmlLibUnderlyingLauncher"/> with the platform-default
    /// <c>.minecraft</c> directory. Used by the App's manual DI in <c>App.axaml.cs</c>.
    /// </summary>
    public static CmlLibMinecraftLauncherService Create(ILauncherLogger logger)
        => new(new CmlLibUnderlyingLauncher(), logger);

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
    public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            var ex = new AuthenticationFailedException("Username is required for authentication.");
            _logger.Error("AuthenticateAsync rejected: empty username.", ex);
            throw ex;
        }

        switch (request.Mode)
        {
            case AuthMode.Offline:
                var session = MSession.CreateOfflineSession(request.Username);
                _logger.Info($"Offline session created for '{request.Username}'.");
                return Task.FromResult(new AuthResult
                {
                    Username = session.Username ?? request.Username,
                    Uuid = session.UUID ?? string.Empty,
                    AccessToken = session.AccessToken ?? string.Empty,
                    IsOffline = true,
                });

            case AuthMode.Microsoft:
            {
                var ex = new AuthenticationFailedException(
                    "Microsoft account authentication is not implemented in v0.1. " +
                    "It requires an Azure-app Client ID; tracked for v0.2. Use AuthMode.Offline for now.");
                _logger.Error("AuthenticateAsync rejected: Microsoft auth not implemented.", ex);
                throw ex;
            }

            case AuthMode.Mojang:
            {
                var ex = new AuthenticationFailedException(
                    "Mojang username+password authentication was discontinued by Mojang in 2022 " +
                    "and is not supported by this launcher. Use AuthMode.Offline.");
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
