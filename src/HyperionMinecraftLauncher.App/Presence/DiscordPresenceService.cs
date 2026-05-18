using System;
using DiscordRPC;
using DiscordRPC.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Presence;

/// <summary>
/// Production <see cref="IPresenceService"/> backed by the
/// <a href="https://github.com/Lachee/discord-rpc-csharp">DiscordRichPresence</a> NuGet,
/// which talks to a running Discord client over a local named-pipe.
/// </summary>
/// <remarks>
/// Every public call is wrapped in <c>try / catch</c> with logging via
/// <see cref="ILauncherLogger.Warn(string)"/>: Discord may not be running, the pipe may
/// fail to open, or the library may throw mid-write. None of those failures should kill
/// the launcher - Rich Presence is purely cosmetic.
/// </remarks>
public sealed class DiscordPresenceService : IPresenceService, IDisposable
{
    // TODO: Replace with real Tech Tea Studio Discord app id.
    private const string ApplicationId = "1234567890123456789";

    /// <summary>Asset key for the large image (must match the Discord app's "Rich Presence Assets" upload).</summary>
    private const string LargeImageKey = "hyperion_icon";

    /// <summary>Asset key for the small image shown while a game is running.</summary>
    private const string SmallImageKeyPlaying = "mc_grass";

    private readonly ILauncherLogger _logger;
    private DiscordRpcClient? _client;
    private bool _disposed;

    /// <summary>Create the service. Connection to Discord happens lazily on first presence call.</summary>
    public DiscordPresenceService(ILauncherLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void SetIdle()
    {
        try
        {
            EnsureClient();
            _client?.SetPresence(new RichPresence
            {
                Details = "In Hyperion launcher",
                Assets = new Assets
                {
                    LargeImageKey = LargeImageKey,
                    LargeImageText = "Hyperion Minecraft Launcher",
                },
            });
        }
        catch (Exception ex)
        {
            _logger.Warn($"Discord presence SetIdle failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void SetPlaying(string versionId, string? instanceName)
    {
        try
        {
            EnsureClient();
            var state = string.IsNullOrWhiteSpace(instanceName)
                ? $"Playing {versionId}"
                : $"Playing {versionId} - {instanceName}";

            _client?.SetPresence(new RichPresence
            {
                Details = state,
                Assets = new Assets
                {
                    LargeImageKey = LargeImageKey,
                    LargeImageText = "Hyperion Minecraft Launcher",
                    SmallImageKey = SmallImageKeyPlaying,
                    SmallImageText = "In-game",
                },
                Timestamps = Timestamps.Now,
            });
        }
        catch (Exception ex)
        {
            _logger.Warn($"Discord presence SetPlaying failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        try
        {
            if (_client is not null)
            {
                try { _client.ClearPresence(); } catch { /* best-effort */ }
                _client.Deinitialize();
                _client.Dispose();
                _client = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Discord presence Stop failed: {ex.Message}");
        }
    }

    private void EnsureClient()
    {
        if (_disposed) return;
        if (_client is { IsInitialized: true }) return;

        // Fresh client - the library doesn't recover well from a half-initialized state,
        // so re-instantiate when a previous Stop() tore down the connection.
        _client = new DiscordRpcClient(ApplicationId)
        {
            Logger = new NullLogger(),
        };
        _client.Initialize();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
