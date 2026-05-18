using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Presence;

/// <summary>
/// v0.32.1 (T-startup-perf): an <see cref="IPresenceService"/> proxy that records the most
/// recent presence call until the real underlying service is constructed (on a background
/// thread, so its Discord-IPC handshake doesn't gate window first-paint). Once
/// <see cref="Attach"/> is called the proxy replays the last buffered state to the underlying
/// and forwards every subsequent call directly.
/// </summary>
/// <remarks>
/// Only the LAST state matters - if the VM calls <c>SetIdle</c> then <c>SetPlaying("1.21.5", null)</c>
/// before Discord is up, we only need to replay the playing state. This matches how Rich Presence
/// itself works: a single current state per client.
/// </remarks>
public sealed class DeferredPresenceService : IPresenceService
{
    private readonly object _gate = new();
    private readonly ILauncherLogger _logger;
    private IPresenceService? _real;
    private bool _stopped;

    /// <summary>The last call we received before <see cref="Attach"/>. <c>null</c> means "no state set yet".</summary>
    private Action<IPresenceService>? _pending;

    public DeferredPresenceService(ILauncherLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Swap in the real backend (e.g. <see cref="DiscordPresenceService"/>) once it's finished
    /// constructing on a background thread. Replays the last pending state through it, then
    /// every future call is forwarded directly. Safe to call once; subsequent calls are ignored.
    /// </summary>
    public void Attach(IPresenceService real)
    {
        ArgumentNullException.ThrowIfNull(real);
        Action<IPresenceService>? toReplay;
        lock (_gate)
        {
            if (_real is not null) return; // already attached
            _real = real;
            if (_stopped)
            {
                // The host already asked us to Stop before the real backend was ready; honour that.
                toReplay = static svc => { try { svc.Stop(); } catch { /* cosmetic */ } };
                _pending = null;
            }
            else
            {
                toReplay = _pending;
                _pending = null;
            }
        }
        if (toReplay is not null)
        {
            try { toReplay(real); }
            catch (Exception ex) { _logger.Warn($"Deferred presence replay failed: {ex.Message}"); }
        }
    }

    /// <inheritdoc />
    public void SetIdle()
    {
        IPresenceService? r;
        lock (_gate)
        {
            r = _real;
            if (r is null)
            {
                _pending = static svc => svc.SetIdle();
                return;
            }
        }
        try { r.SetIdle(); }
        catch (Exception ex) { _logger.Warn($"Presence SetIdle failed: {ex.Message}"); }
    }

    /// <inheritdoc />
    public void SetPlaying(string versionId, string? instanceName)
    {
        IPresenceService? r;
        lock (_gate)
        {
            r = _real;
            if (r is null)
            {
                // Capture by value (string is immutable; instanceName is a snapshot).
                var capturedVersion = versionId;
                var capturedInstance = instanceName;
                _pending = svc => svc.SetPlaying(capturedVersion, capturedInstance);
                return;
            }
        }
        try { r.SetPlaying(versionId, instanceName); }
        catch (Exception ex) { _logger.Warn($"Presence SetPlaying failed: {ex.Message}"); }
    }

    /// <inheritdoc />
    public void Stop()
    {
        IPresenceService? r;
        lock (_gate)
        {
            _stopped = true;
            r = _real;
            // Drop any pending state - we're stopping, not transitioning.
            _pending = null;
        }
        try { r?.Stop(); }
        catch (Exception ex) { _logger.Warn($"Presence Stop failed: {ex.Message}"); }
    }
}
