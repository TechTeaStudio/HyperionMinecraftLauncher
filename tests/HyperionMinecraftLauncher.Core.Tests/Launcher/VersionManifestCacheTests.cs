using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// v0.32.1 (T-startup-perf): the version manifest is the single biggest cold-start
/// network hit (~1.5 s). The Core service consults an optional <see cref="FileCache"/>
/// before calling the underlying launcher's manifest fetch. These tests pin the contract:
/// fresh cache wins; stale cache is rescued on failure; first cold launch writes back.
/// </summary>
public sealed class VersionManifestCacheTests : IDisposable
{
    private readonly string _cacheDir;

    public VersionManifestCacheTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "hml-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListVersionsAsync_NoCache_HitsUnderlying()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[]
            {
                new VersionMetadata { Name = "1.21.5", Type = "release" },
            },
        };
        var svc = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());

        var result = await svc.ListVersionsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("1.21.5", result[0].Name);
    }

    [Fact]
    public async Task ListVersionsAsync_ColdCache_FetchesAndWritesBack()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[]
            {
                new VersionMetadata { Name = "1.21.5", Type = "release" },
                new VersionMetadata { Name = "1.21.4", Type = "release" },
            },
        };
        var cache = new FileCache(_cacheDir);
        var svc = new CmlLibMinecraftLauncherService(fake, new RecordingLogger(), versionManifestCache: cache);

        var first = await svc.ListVersionsAsync(CancellationToken.None);

        Assert.Equal(2, first.Count);
        // Cache file should exist after the first fetch.
        Assert.True(File.Exists(cache.PathFor("versions.json")));
    }

    [Fact]
    public async Task ListVersionsAsync_FreshCache_DoesNotHitUnderlying()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[] { new VersionMetadata { Name = "1.21.5", Type = "release" } },
        };
        var cache = new FileCache(_cacheDir);
        var svc = new CmlLibMinecraftLauncherService(fake, new RecordingLogger(), versionManifestCache: cache);

        // Cold call populates the cache.
        await svc.ListVersionsAsync(CancellationToken.None);
        Assert.Equal(1, fake.GetAllVersionsCallCount);

        // Subsequent calls within the TTL should be served from disk, NOT hit the underlying.
        var warm = await svc.ListVersionsAsync(CancellationToken.None);
        Assert.Equal("1.21.5", warm[0].Name);
        Assert.Equal(1, fake.GetAllVersionsCallCount);
    }

    [Fact]
    public async Task ListVersionsAsync_ExpiredCache_RefetchesFromUnderlying()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[] { new VersionMetadata { Name = "1.21.5", Type = "release" } },
        };
        var cache = new FileCache(_cacheDir);
        // 0-tick TTL: any cached entry is immediately stale; we want to verify the underlying is hit again.
        var svc = new CmlLibMinecraftLauncherService(
            fake,
            new RecordingLogger(),
            versionManifestCache: cache,
            versionManifestTtl: TimeSpan.Zero);

        await svc.ListVersionsAsync(CancellationToken.None);
        await svc.ListVersionsAsync(CancellationToken.None);

        Assert.Equal(2, fake.GetAllVersionsCallCount);
    }

    [Fact]
    public async Task ListVersionsAsync_NetworkFailure_ServesStaleCachedCopy()
    {
        var fake = new FakeUnderlyingLauncher
        {
            VersionsToReturn = new[] { new VersionMetadata { Name = "1.21.5", Type = "release" } },
        };
        var cache = new FileCache(_cacheDir);
        var svc = new CmlLibMinecraftLauncherService(
            fake,
            new RecordingLogger(),
            versionManifestCache: cache,
            versionManifestTtl: TimeSpan.Zero);

        // Seed the cache.
        await svc.ListVersionsAsync(CancellationToken.None);

        // Subsequent fetches fail (offline).
        fake.ThrowOnVersions = new HttpRequestException("network down");
        var stale = await svc.ListVersionsAsync(CancellationToken.None);

        Assert.Single(stale);
        Assert.Equal("1.21.5", stale[0].Name);
    }

    [Fact]
    public async Task ListVersionsAsync_NetworkFailure_NoCache_StillThrows()
    {
        var fake = new FakeUnderlyingLauncher
        {
            ThrowOnVersions = new HttpRequestException("network down"),
        };
        var svc = new CmlLibMinecraftLauncherService(fake, new RecordingLogger());

        await Assert.ThrowsAsync<InstallationFailedException>(
            () => svc.ListVersionsAsync(CancellationToken.None));
    }
}
