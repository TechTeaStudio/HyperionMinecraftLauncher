using System;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Util;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// T-stdout-pipe (v0.32.1): verify the stdout/stderr piping wire from
/// <see cref="LaunchResult.GameLogStream"/> to <see cref="MainViewModel.Append"/>.
/// </summary>
/// <remarks>
/// We drive a hand-built <see cref="Subject{T}"/> through a fake launcher service so the test
/// never depends on a real CmlLib install, Java, or child process. The full launch path is
/// exercised by <see cref="MainViewModelTests"/>; this file focuses on the new observable
/// bridge and the Subject shim itself.
/// </remarks>
public class GameLogPipingTests
{
    [Fact]
    public async Task SubscribeToGameLogAsync_StdoutLines_AppendedWithGamePrefix()
    {
        var subject = new Subject<GameLogLine>();
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger());

        var pipeTask = vm.SubscribeToGameLogAsync(subject);

        subject.OnNext(new GameLogLine("Setting user: Steve", GameLogStream.Stdout, DateTimeOffset.UtcNow));
        subject.OnNext(new GameLogLine("LWJGL Version: 3.3.3", GameLogStream.Stdout, DateTimeOffset.UtcNow));
        subject.OnNext(new GameLogLine("OpenAL initialized", GameLogStream.Stdout, DateTimeOffset.UtcNow));

        Assert.Contains("[game] Setting user: Steve", vm.LogText);
        Assert.Contains("[game] LWJGL Version: 3.3.3", vm.LogText);
        Assert.Contains("[game] OpenAL initialized", vm.LogText);

        subject.OnCompleted();
        await pipeTask;

        // After OnCompleted the bridge appends a clear marker so the user sees the game has exited.
        Assert.Contains("(process exited)", vm.LogText);
    }

    [Fact]
    public async Task SubscribeToGameLogAsync_StderrLines_AppendedWithGamePrefix()
    {
        var subject = new Subject<GameLogLine>();
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger());

        var pipeTask = vm.SubscribeToGameLogAsync(subject);

        subject.OnNext(new GameLogLine("Exception in thread \"main\"", GameLogStream.Stderr, DateTimeOffset.UtcNow));
        subject.OnNext(new GameLogLine("\tat net.minecraft.client.Main.main(Main.java:42)", GameLogStream.Stderr, DateTimeOffset.UtcNow));

        Assert.Contains("[game] Exception in thread \"main\"", vm.LogText);
        Assert.Contains("[game] \tat net.minecraft.client.Main.main(Main.java:42)", vm.LogText);

        subject.OnCompleted();
        await pipeTask;
    }

    [Fact]
    public async Task LaunchAsync_ServicePropagatesGameLogStream_ToLaunchResult()
    {
        // The CmlLibMinecraftLauncherService should pass IUnderlyingLauncher's StartProcessResult.GameLogStream
        // through to LaunchResult.GameLogStream verbatim, and should pass the request.CaptureGameLog flag down.
        var fake = new FakeUnderlyingLauncher
        {
            ProcessIdToReturn = 4242,
            GameLogStreamToReturn = new Subject<GameLogLine>(),
        };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.AuthRequest
            {
                Mode = TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.AuthMode.Offline,
                Username = "Steve",
            },
            System.Threading.CancellationToken.None);

        var result = await service.LaunchAsync(
            new LaunchRequest { VersionName = "1.21.5", Session = auth, CaptureGameLog = true },
            progress: null,
            System.Threading.CancellationToken.None);

        Assert.Equal(true, fake.LastCaptureGameLog);
        Assert.NotNull(result.GameLogStream);
        Assert.Same(fake.GameLogStreamToReturn, result.GameLogStream);
    }

    [Fact]
    public async Task LaunchAsync_CaptureGameLogFalse_ServicePassesFalseAndStreamStaysNull()
    {
        var fake = new FakeUnderlyingLauncher { ProcessIdToReturn = 1, GameLogStreamToReturn = null };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(
            new TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.AuthRequest
            {
                Mode = TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.AuthMode.Offline,
                Username = "Steve",
            },
            System.Threading.CancellationToken.None);

        var result = await service.LaunchAsync(
            new LaunchRequest { VersionName = "1.21.5", Session = auth, CaptureGameLog = false },
            progress: null,
            System.Threading.CancellationToken.None);

        Assert.Equal(false, fake.LastCaptureGameLog);
        Assert.Null(result.GameLogStream);
    }
}
