using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.App.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Presence;

/// <summary>
/// v0.32.1: <see cref="DeferredPresenceService"/> lets the launcher hand a working presence proxy
/// to the VM before Discord IPC has finished its named-pipe handshake. These tests pin the
/// "buffer, then replay last state" contract.
/// </summary>
public sealed class DeferredPresenceServiceTests
{
    [Fact]
    public void Attach_NoPriorCalls_DoesNotInvokeUnderlying()
    {
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());

        deferred.Attach(underlying);

        Assert.Empty(underlying.Events);
    }

    [Fact]
    public void Attach_AfterSetIdle_ReplaysIdleState()
    {
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());

        deferred.SetIdle();
        deferred.Attach(underlying);

        Assert.Equal(new[] { "Idle" }, underlying.Events);
    }

    [Fact]
    public void Attach_AfterMultipleCalls_OnlyReplaysLastState()
    {
        // Rich Presence is single-state: there's no point replaying every step the VM
        // went through before Discord came up, only the latest snapshot.
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());

        deferred.SetIdle();
        deferred.SetPlaying("1.21.5", "My Instance");
        deferred.Attach(underlying);

        Assert.Equal(new[] { "Playing(1.21.5, My Instance)" }, underlying.Events);
    }

    [Fact]
    public void CallsAfterAttach_AreForwardedDirectly()
    {
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());
        deferred.Attach(underlying);

        deferred.SetIdle();
        deferred.SetPlaying("1.21.5", null);

        Assert.Equal(new[] { "Idle", "Playing(1.21.5, <null>)" }, underlying.Events);
    }

    [Fact]
    public void Stop_BeforeAttach_ProxiesStopOnAttach()
    {
        // The host closing the window before Discord came up still needs to result in Stop()
        // being called on the underlying once it's attached. Pending Set* state is dropped.
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());

        deferred.SetIdle();
        deferred.Stop();
        deferred.Attach(underlying);

        Assert.Equal(new[] { "Stop" }, underlying.Events);
    }

    [Fact]
    public void Attach_IsIdempotent()
    {
        var underlying = new RecordingPresenceService();
        var deferred = new DeferredPresenceService(new MutedLogger());

        deferred.SetIdle();
        deferred.Attach(underlying);
        deferred.Attach(new RecordingPresenceService()); // second Attach is a no-op

        deferred.SetIdle();
        Assert.Equal(new[] { "Idle", "Idle" }, underlying.Events);
    }

    private sealed class RecordingPresenceService : IPresenceService
    {
        public List<string> Events { get; } = new();
        public void SetIdle() => Events.Add("Idle");
        public void SetPlaying(string versionId, string? instanceName)
            => Events.Add($"Playing({versionId}, {instanceName ?? "<null>"})");
        public void Stop() => Events.Add("Stop");
    }

    private sealed class MutedLogger : ILauncherLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
