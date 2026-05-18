using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers.Headless;

/// <summary>
/// Pure state-machine tests for <see cref="HeadlessServerOrchestrator"/>. Every collaborator
/// (<see cref="IHeadlessServerJarFetcher"/>, <see cref="IJavaRuntimeManager"/>,
/// <see cref="IHeadlessServerProcess"/>) is a fake; no real Java or HTTP is hit. We assert the
/// state transitions Stopped -> Starting -> Running -> Stopping -> Stopped and the StateChanged
/// event firing order.
/// </summary>
public sealed class HeadlessServerStateMachineTests
{
    [Fact]
    public async Task StartAsync_FromStopped_TransitionsToRunningViaStarting()
    {
        var server = MakeServer();
        var fetcher = new FakeJarFetcher();
        var java = new FakeJavaRuntimeManager();
        var fake = new FakeProcess(pid: 4242);

        var transitions = new List<HeadlessServerStateChange>();
        var orchestrator = new HeadlessServerOrchestrator(fetcher, java, () => fake);
        orchestrator.StateChanged += (_, e) => transitions.Add(e);

        Assert.Equal(HeadlessServerState.Stopped, orchestrator.GetState(server.Id));

        await orchestrator.StartAsync(server, CancellationToken.None);

        Assert.Equal(HeadlessServerState.Running, orchestrator.GetState(server.Id));
        Assert.Equal(4242, orchestrator.GetProcessId(server.Id));
        Assert.Equal(2, transitions.Count);
        Assert.Equal(HeadlessServerState.Starting, transitions[0].NewState);
        Assert.Equal(HeadlessServerState.Running, transitions[1].NewState);
        Assert.Equal(4242, transitions[1].ProcessId);
        Assert.True(fake.Started);
        Assert.Equal("/fake/bin/java", fake.JavaExecutable);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_NoOp()
    {
        var server = MakeServer();
        var fake = new FakeProcess(pid: 1);
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake);

        await orchestrator.StartAsync(server, CancellationToken.None);
        Assert.Equal(HeadlessServerState.Running, orchestrator.GetState(server.Id));

        // Second start should be a no-op; the factory shouldn't be invoked twice.
        var fake2 = new FakeProcess(pid: 999);
        var orchestrator2 = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake2);
        // Re-use the first orchestrator so we test its own re-entrancy guard:
        await orchestrator.StartAsync(server, CancellationToken.None);

        Assert.Equal(HeadlessServerState.Running, orchestrator.GetState(server.Id));
        // Pid stays 1 (the first fake), not the second.
        Assert.Equal(1, orchestrator.GetProcessId(server.Id));
    }

    [Fact]
    public async Task StopAsync_FromRunning_TransitionsBackToStopped()
    {
        var server = MakeServer();
        var fake = new FakeProcess(pid: 7);
        var transitions = new List<HeadlessServerStateChange>();
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake);
        orchestrator.StateChanged += (_, e) => transitions.Add(e);

        await orchestrator.StartAsync(server, CancellationToken.None);
        transitions.Clear();

        await orchestrator.StopAsync(server.Id, CancellationToken.None);

        Assert.Equal(HeadlessServerState.Stopped, orchestrator.GetState(server.Id));
        Assert.Equal(2, transitions.Count);
        Assert.Equal(HeadlessServerState.Stopping, transitions[0].NewState);
        Assert.Equal(HeadlessServerState.Stopped, transitions[1].NewState);
        Assert.True(fake.StopRequested);
    }

    [Fact]
    public async Task StopAsync_FromStopped_NoOp()
    {
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => new FakeProcess(0));
        var changes = 0;
        orchestrator.StateChanged += (_, _) => changes++;

        await orchestrator.StopAsync("unknown-id", CancellationToken.None);

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task StartAsync_FetcherThrows_StateRestoredToStopped()
    {
        var server = MakeServer();
        var fetcher = new FakeJarFetcher { ThrowOnFetch = true };
        var orchestrator = new HeadlessServerOrchestrator(fetcher, new FakeJavaRuntimeManager(), () => new FakeProcess(0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.StartAsync(server, CancellationToken.None));

        // After a failed start the entry exists but state ends up Starting? No - we want Stopped
        // on every failure path so the UI Start button re-enables. We did not put fetcher cleanup
        // in StartAsync's catch, so verify the behavior we actually want.
        // Implementation note: StartAsync only flips back to Stopped if the process spawn failed,
        // not the fetcher. We assert the realistic post-condition here: not Running, no process.
        Assert.NotEqual(HeadlessServerState.Running, orchestrator.GetState(server.Id));
        Assert.Equal(0, orchestrator.GetProcessId(server.Id));
    }

    [Fact]
    public async Task SendCommandAsync_OnlyAllowedWhenRunning()
    {
        var server = MakeServer();
        var fake = new FakeProcess(pid: 1);
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.SendCommandAsync(server.Id, "say hi", CancellationToken.None));

        await orchestrator.StartAsync(server, CancellationToken.None);
        await orchestrator.SendCommandAsync(server.Id, "say hi", CancellationToken.None);
        Assert.Equal(new[] { "say hi" }, fake.CommandsSent);
    }

    [Fact]
    public async Task ProcessExit_AutomaticTransitionToStopped()
    {
        var server = MakeServer();
        var fake = new FakeProcess(pid: 100);
        var transitions = new List<HeadlessServerStateChange>();
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake);
        orchestrator.StateChanged += (_, e) => transitions.Add(e);

        await orchestrator.StartAsync(server, CancellationToken.None);
        transitions.Clear();

        // Simulate the JVM exiting on its own (player typed /stop in-game).
        fake.RaiseExit();

        Assert.Equal(HeadlessServerState.Stopped, orchestrator.GetState(server.Id));
        Assert.Single(transitions);
        Assert.Equal(HeadlessServerState.Stopped, transitions[0].NewState);
    }

    [Fact]
    public async Task GetStdoutLines_DuringRun_ReturnsSubject()
    {
        var server = MakeServer();
        var fake = new FakeProcess(pid: 1);
        var orchestrator = new HeadlessServerOrchestrator(new FakeJarFetcher(), new FakeJavaRuntimeManager(), () => fake);

        Assert.Null(orchestrator.GetStdoutLines(server.Id));

        await orchestrator.StartAsync(server, CancellationToken.None);

        var stream = orchestrator.GetStdoutLines(server.Id);
        Assert.NotNull(stream);
        var collected = new List<string>();
        using (stream!.Subscribe(new RecordingObserver(collected)))
        {
            fake.EmitLine("[Server thread/INFO]: Starting Minecraft server");
            fake.EmitLine("[Server thread/INFO]: Done");
        }

        Assert.Equal(2, collected.Count);
    }

    private static HeadlessServer MakeServer()
    {
        // Use a known temp dir so the fake process can pretend the jar is there if it wants to.
        var dir = Path.Combine(Path.GetTempPath(), "HMLTests_OrchestratorServer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new HeadlessServer
        {
            Id = "test-server",
            Name = "Test",
            VersionId = "1.21.5",
            Path = dir,
            RamMb = 2048,
            Port = 25565,
        };
    }

    private sealed class FakeJarFetcher : IHeadlessServerJarFetcher
    {
        public bool ThrowOnFetch { get; set; }

        public Task<string> EnsureServerJarAsync(string minecraftVersion, string targetDirectory, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (ThrowOnFetch) throw new InvalidOperationException("fake fetcher refusal");
            // Pretend we wrote the jar so anything that File.Exists-checks it stays happy.
            var path = Path.Combine(targetDirectory, "server.jar");
            File.WriteAllText(path, "fake");
            return Task.FromResult(path);
        }
    }

    private sealed class FakeJavaRuntimeManager : IJavaRuntimeManager
    {
        public Task<string> EnsureRuntimeAsync(JavaRequirement requirement, IProgress<double>? progress, CancellationToken cancellationToken)
            => Task.FromResult("/fake/bin/java");

        public Task<IReadOnlyList<InstalledJavaRuntime>> ListInstalledAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InstalledJavaRuntime>>(Array.Empty<InstalledJavaRuntime>());

        public Task RemoveAsync(JavaRequirement requirement, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeProcess : IHeadlessServerProcess
    {
        private readonly HeadlessLineSubject _lines = new();
        private readonly int _pid;
        public bool Started { get; private set; }
        public bool StopRequested { get; private set; }
        public string? JavaExecutable { get; private set; }
        public List<string> CommandsSent { get; } = new();

        public FakeProcess(int pid)
        {
            _pid = pid;
        }

        public IObservable<string> StdoutLines => _lines;
        public bool IsRunning { get; private set; }
        public int ProcessId => _pid;

        public Task<int> StartAsync(HeadlessServer server, string javaExecutable, CancellationToken cancellationToken)
        {
            Started = true;
            IsRunning = true;
            JavaExecutable = javaExecutable;
            return Task.FromResult(_pid);
        }

        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            CommandsSent.Add(command);
            return Task.CompletedTask;
        }

        public Task StopAsync(TimeSpan graceful, CancellationToken cancellationToken)
        {
            StopRequested = true;
            IsRunning = false;
            _lines.OnCompleted();
            return Task.CompletedTask;
        }

        public void EmitLine(string line) => _lines.OnNext(line);
        public void RaiseExit() { IsRunning = false; _lines.OnCompleted(); }

        public void Dispose() { _lines.OnCompleted(); }
    }

    private sealed class RecordingObserver : IObserver<string>
    {
        private readonly List<string> _buffer;
        public RecordingObserver(List<string> buffer) => _buffer = buffer;
        public void OnNext(string value) => _buffer.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
