using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Java;

/// <summary>
/// Verify the v0.32.11 system-probe path in <see cref="AdoptiumJavaRuntimeManager"/>:
/// when an <see cref="ISystemJavaProbe"/> is wired and returns a matching java path,
/// <see cref="AdoptiumJavaRuntimeManager.EnsureRuntimeAsync"/> must return that path
/// without firing an HTTP download.
/// </summary>
public class AdoptiumJavaRuntimeManagerSystemProbeTests
{
    [Fact]
    public async Task EnsureRuntimeAsync_ProbeReturnsPath_SkipsDownload()
    {
        var tempRoot = NewEmptyRoot();
        try
        {
            var fakeJavaExe = Path.Combine(tempRoot, "fake-system-java.exe");
            File.WriteAllText(fakeJavaExe, "stub"); // doesn't have to be a real binary; the manager doesn't execute it.

            var explodingHandler = new ExplodingHttpMessageHandler();
            using var http = new HttpClient(explodingHandler);

            var probe = new FakeSystemJavaProbe(fakeJavaExe);
            var manager = new AdoptiumJavaRuntimeManager(http, tempRoot, "windows", "x64", probe);

            var result = await manager.EnsureRuntimeAsync(JavaRequirement.Java21, progress: null, CancellationToken.None);

            Assert.Equal(fakeJavaExe, result);
            Assert.False(explodingHandler.WasCalled,
                "The Adoptium HTTP download must not run when the system probe returns a matching path.");
            Assert.Equal(JavaRequirement.Java21, probe.LastRequestedRequirement);
        }
        finally
        {
            TryDeleteDir(tempRoot);
        }
    }

    [Fact]
    public async Task EnsureRuntimeAsync_ProbeReturnsNull_FallsThroughToDownload()
    {
        var tempRoot = NewEmptyRoot();
        try
        {
            var explodingHandler = new ExplodingHttpMessageHandler();
            using var http = new HttpClient(explodingHandler);

            var probe = new FakeSystemJavaProbe(matchPath: null);
            var manager = new AdoptiumJavaRuntimeManager(http, tempRoot, "windows", "x64", probe);

            // The exploding handler throws on first request - which is exactly what we want to
            // observe. If the probe path were short-circuiting us, the download would never run
            // and we'd never see the exception.
            await Assert.ThrowsAnyAsync<Exception>(() =>
                manager.EnsureRuntimeAsync(JavaRequirement.Java17, progress: null, CancellationToken.None));

            Assert.True(explodingHandler.WasCalled,
                "When the probe returns null, the download path must still run.");
        }
        finally
        {
            TryDeleteDir(tempRoot);
        }
    }

    [Fact]
    public async Task EnsureRuntimeAsync_ManagedCacheHit_NeverConsultsProbe()
    {
        var tempRoot = NewEmptyRoot();
        try
        {
            // Pre-seed a fake managed-cache layout: <root>/java21/jdk-21/bin/java.exe.
            var requirementDir = Path.Combine(tempRoot, "java21");
            var jdkBinDir = Path.Combine(requirementDir, "jdk-21", "bin");
            Directory.CreateDirectory(jdkBinDir);
            var cachedExe = Path.Combine(jdkBinDir, "java.exe");
            File.WriteAllText(cachedExe, "stub");

            var explodingHandler = new ExplodingHttpMessageHandler();
            using var http = new HttpClient(explodingHandler);

            var probe = new FakeSystemJavaProbe(matchPath: "/should/not/be/used");
            var manager = new AdoptiumJavaRuntimeManager(http, tempRoot, "windows", "x64", probe);

            var result = await manager.EnsureRuntimeAsync(JavaRequirement.Java21, progress: null, CancellationToken.None);

            Assert.Equal(cachedExe, result);
            Assert.False(probe.WasQueried,
                "Managed-cache hit must short-circuit before the probe runs - the cache is the cheapest check.");
            Assert.False(explodingHandler.WasCalled,
                "Managed-cache hit must short-circuit before any HTTP work runs.");
        }
        finally
        {
            TryDeleteDir(tempRoot);
        }
    }

    private static string NewEmptyRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hyperion-java-probe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// In-memory <see cref="ISystemJavaProbe"/> that records the last requirement queried and
    /// returns a fixed match path (or null). Lets tests assert on call presence + parameter.
    /// </summary>
    private sealed class FakeSystemJavaProbe : ISystemJavaProbe
    {
        private readonly string? _matchPath;
        public bool WasQueried { get; private set; }
        public JavaRequirement? LastRequestedRequirement { get; private set; }

        public FakeSystemJavaProbe(string? matchPath) { _matchPath = matchPath; }

        public Task<string?> FindAsync(JavaRequirement requirement, CancellationToken cancellationToken)
        {
            WasQueried = true;
            LastRequestedRequirement = requirement;
            return Task.FromResult(_matchPath);
        }
    }

    /// <summary>
    /// HTTP handler that throws on the first attempt and records that it was hit. Use it
    /// to assert "no download must run" or "download definitely ran" without spinning up
    /// a real server.
    /// </summary>
    private sealed class ExplodingHttpMessageHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new HttpRequestException(
                $"ExplodingHttpMessageHandler: refusing to issue a real request to {request.RequestUri}.");
        }
    }
}
