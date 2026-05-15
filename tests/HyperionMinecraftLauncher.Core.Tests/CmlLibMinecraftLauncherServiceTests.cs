using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class CmlLibMinecraftLauncherServiceTests
{
    [Fact]
    public async Task ListVersionsAsync_ReturnsUnderlyingListUnchanged()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[]
            {
                new VersionMetadata { Name = "1.21.5", Type = "release", ReleaseTime = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) },
                new VersionMetadata { Name = "1.21.4", Type = "release", ReleaseTime = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) },
            },
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);

        var result = await service.ListVersionsAsync(CancellationToken.None);

        Assert.Equal(new[] { "1.21.5", "1.21.4" }, result.Select(v => v.Name).ToArray());
        Assert.Contains(logger.InfoEntries, l => l.Contains("Loaded 2 versions"));
    }

    [Fact]
    public async Task ListVersionsAsync_WrapsHttpRequestException()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnVersions = new HttpRequestException("dns failure"),
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);

        var ex = await Assert.ThrowsAsync<InstallationFailedException>(() => service.ListVersionsAsync(CancellationToken.None));
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.NotEmpty(logger.ErrorEntries);
    }

    [Fact]
    public async Task AuthenticateAsync_Offline_BuildsSession()
    {
        var fake = new FakeUnderlyingLauncher();
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);

        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        Assert.Equal("Steve", auth.Username);
        Assert.False(string.IsNullOrEmpty(auth.Uuid));
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.True(auth.IsOffline);
    }

    [Fact]
    public async Task AuthenticateAsync_Microsoft_ThrowsWithFriendlyMessage()
    {
        var service = new CmlLibMinecraftLauncherService(new FakeUnderlyingLauncher(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Microsoft, Username = "Steve" }, CancellationToken.None));

        Assert.Contains("Microsoft", ex.Message);
        Assert.Contains("v0.2", ex.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_Mojang_ThrowsWithFriendlyMessage()
    {
        var service = new CmlLibMinecraftLauncherService(new FakeUnderlyingLauncher(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Mojang, Username = "Steve" }, CancellationToken.None));

        Assert.Contains("Mojang", ex.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_EmptyUsername_Throws()
    {
        var service = new CmlLibMinecraftLauncherService(new FakeUnderlyingLauncher(), new RecordingLogger());

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "" }, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_HappyPath_InstallsThenStartsAndReturnsPid()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ProcessIdToReturn = 4242,
            ProgressEventsToEmit = new[]
            {
                new LaunchProgress { Stage = "Downloading library", Fraction = 0.5, CurrentItem = "lwjgl-3.3.3.jar" },
                new LaunchProgress { Stage = "Install done", Fraction = 1.0 },
            },
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);

        var reported = new List<LaunchProgress>();
        var progress = new Progress<LaunchProgress>(p => reported.Add(p));

        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Alex" }, CancellationToken.None);
        var result = await service.LaunchAsync(
            new LaunchRequest { VersionName = "1.21.5", Session = auth },
            progress,
            CancellationToken.None);

        Assert.Equal(4242, result.ProcessId);
        Assert.Equal("1.21.5", result.VersionName);
        Assert.True(fake.InstallCalled);
        Assert.True(fake.StartCalled);
        Assert.True(fake.InstallCalledBeforeStart, "Install must be called before Start");

        // The shim emitted two events; the service added one "Starting game" before StartProcessAsync.
        // Use a small spin-wait because System.Progress<T> uses the synchronization context to post.
        await WaitUntilAsync(() => reported.Count >= 3, TimeSpan.FromSeconds(2));
        Assert.Contains(reported, p => p.Stage == "Downloading library");
        Assert.Contains(reported, p => p.Stage == "Starting game");
    }

    [Fact]
    public async Task LaunchAsync_EmptyVersionName_ThrowsVersionNotFound()
    {
        var service = new CmlLibMinecraftLauncherService(new FakeUnderlyingLauncher(), new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        await Assert.ThrowsAsync<VersionNotFoundException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "", Session = auth }, null, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_KeyNotFoundFromInstall_MapsToVersionNotFound()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnInstall = new KeyNotFoundException("no such version 99.99"),
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<VersionNotFoundException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "99.99", Session = auth }, null, CancellationToken.None));

        Assert.Equal("99.99", ex.VersionName);
        Assert.IsType<KeyNotFoundException>(ex.InnerException);
        Assert.Contains(logger.ErrorEntries, e => e.Contains("99.99"));
    }

    [Fact]
    public async Task LaunchAsync_HttpRequestFromInstall_MapsToInstallationFailed()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnInstall = new HttpRequestException("connection reset"),
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InstallationFailedException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "1.21.5", Session = auth }, null, CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Contains("Network", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.ErrorEntries, e => e.Contains("1.21.5"));
    }

    [Fact]
    public async Task LaunchAsync_IOExceptionFromInstall_MapsToInstallationFailed()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnInstall = new IOException("disk full"),
        };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InstallationFailedException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "1.21.5", Session = auth }, null, CancellationToken.None));

        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task LaunchAsync_Win32FromStart_MapsToGameProcessStart()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnStart = new System.ComponentModel.Win32Exception("java not found"),
        };
        var logger = new RecordingLogger();
        var service = new CmlLibMinecraftLauncherService(fake, logger);
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<GameProcessStartException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "1.21.5", Session = auth }, null, CancellationToken.None));

        Assert.IsType<System.ComponentModel.Win32Exception>(ex.InnerException);
        Assert.Contains("Java", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchAsync_CancellationPropagates()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnInstall = new OperationCanceledException(),
        };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "1.21.5", Session = auth }, null, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_GenericExceptionFromInstall_WrappedAsInstallationFailed()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnInstall = new InvalidOperationException("something else"),
        };
        var service = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());
        var auth = await service.AuthenticateAsync(new AuthRequest { Mode = AuthMode.Offline, Username = "Steve" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InstallationFailedException>(() =>
            service.LaunchAsync(new LaunchRequest { VersionName = "1.21.5", Session = auth }, null, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
    }
}

internal sealed class FakeUnderlyingLauncher : IUnderlyingLauncher
{
    public IReadOnlyList<VersionMetadata> VersionsToReturn { get; set; } = Array.Empty<VersionMetadata>();
    public Exception? ThrowOnVersions { get; set; }
    public Exception? ThrowOnInstall { get; set; }
    public Exception? ThrowOnStart { get; set; }
    public int ProcessIdToReturn { get; set; } = 1;
    public IReadOnlyList<LaunchProgress> ProgressEventsToEmit { get; set; } = Array.Empty<LaunchProgress>();

    public bool InstallCalled { get; private set; }
    public bool StartCalled { get; private set; }
    public bool InstallCalledBeforeStart { get; private set; }

    public Task<IReadOnlyList<VersionMetadata>> GetAllVersionsAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnVersions is not null) throw ThrowOnVersions;
        return Task.FromResult(VersionsToReturn);
    }

    public Task InstallAsync(string versionName, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        InstallCalled = true;
        foreach (var ev in ProgressEventsToEmit)
            progress?.Report(ev);

        if (ThrowOnInstall is not null) throw ThrowOnInstall;
        return Task.CompletedTask;
    }

    public Task<int> StartProcessAsync(
        string versionName,
        string username,
        string uuid,
        string accessToken,
        int? minimumRamMb,
        int? maximumRamMb,
        CancellationToken cancellationToken)
    {
        StartCalled = true;
        InstallCalledBeforeStart = InstallCalled;

        if (ThrowOnStart is not null) throw ThrowOnStart;
        return Task.FromResult(ProcessIdToReturn);
    }
}

internal sealed class RecordingLogger : ILauncherLogger
{
    public List<string> InfoEntries { get; } = new();
    public List<string> WarnEntries { get; } = new();
    public List<string> ErrorEntries { get; } = new();

    public void Info(string message) => InfoEntries.Add(message);
    public void Warn(string message) => WarnEntries.Add(message);
    public void Error(string message, Exception? exception = null) => ErrorEntries.Add(message);
}
