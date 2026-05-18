using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods.CurseForge;

/// <summary>
/// v0.32.1 (T-cf-onboarding) regression suite. Before this work the
/// <see cref="CurseForgeRepository"/> captured the API key in its constructor, so
/// pasting a fresh key into the Settings page didn't take effect until the user
/// restarted the launcher. The fix swaps the captured field for a <see cref="Func{TResult}"/>
/// the App can mutate live. These tests pin that contract: a key flip between two
/// <see cref="CurseForgeRepository.SearchAsync"/> calls must be picked up without
/// rebuilding the repository.
/// </summary>
public class CurseForgeRepositoryKeyChangeTests
{
    private const string EmptySearchJson = """{ "data": [] }""";

    [Fact]
    public async Task SearchAsync_PicksUpFreshKeyAfterProviderFlip()
    {
        // The "live key" slot - the App.axaml.cs LiveCurseForgeKey holder is mocked here as
        // a single mutable string the closure captures by reference.
        var keySlot = new MutableString(string.Empty);
        var capturedKeys = new List<string?>();

        var handler = new CapturingHandler((req, _) =>
        {
            req.Headers.TryGetValues("x-api-key", out var vals);
            capturedKeys.Add(vals is null ? null : string.Join(",", vals));
            return Reply(EmptySearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, keySlot.Read, new RecordingLogger());

        // First call: empty key -> network not touched, empty result.
        var first = await repo.SearchAsync(new ModSearchQuery { Query = "jei" }, CancellationToken.None);
        Assert.Empty(first);
        Assert.Empty(capturedKeys);

        // Simulate the user finishing the onboarding dialog and the VM pushing the new key
        // into the live slot. Critically, we do NOT rebuild the repository.
        keySlot.Set("fresh-key-12345");

        // Second call: the repo must now hit CurseForge with the fresh key in x-api-key.
        var second = await repo.SearchAsync(new ModSearchQuery { Query = "jei" }, CancellationToken.None);
        Assert.Empty(second); // Stub returns an empty `data` array; the assertion is that we actually hit the wire.
        var key = Assert.Single(capturedKeys);
        Assert.Equal("fresh-key-12345", key);
    }

    [Fact]
    public async Task SearchAsync_RevertingToEmptyKeyDisablesAgain()
    {
        var keySlot = new MutableString("temporary-key");
        var hitCount = 0;
        var handler = new CapturingHandler((_, _) =>
        {
            hitCount++;
            return Reply(EmptySearchJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, keySlot.Read, new RecordingLogger());

        await repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None);
        Assert.Equal(1, hitCount);

        // User clears the key (e.g. typed a wrong value and bailed out). The next call must
        // short-circuit to the empty-result path instead of sending an unauthenticated request.
        keySlot.Set(string.Empty);
        await repo.SearchAsync(new ModSearchQuery { Query = "x" }, CancellationToken.None);
        Assert.Equal(1, hitCount);
    }

    [Fact]
    public async Task ListFilesAsync_PicksUpFreshKeyAfterProviderFlip()
    {
        var keySlot = new MutableString(string.Empty);
        const string filesJson = """{ "data": [] }""";
        var hitCount = 0;
        var handler = new CapturingHandler((_, _) =>
        {
            hitCount++;
            return Reply(filesJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.curseforge.com/v1/") };
        var repo = new CurseForgeRepository(http, keySlot.Read, new RecordingLogger());

        // Empty key -> NotSupported before any HTTP call.
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            repo.ListFilesAsync("238222", null, null, CancellationToken.None));
        Assert.Equal(0, hitCount);

        // After the user pastes a key, ListFilesAsync must succeed on the same repo instance.
        keySlot.Set("fresh-key");
        var files = await repo.ListFilesAsync("238222", null, null, CancellationToken.None);
        Assert.Empty(files);
        Assert.Equal(1, hitCount);
    }

    private static HttpResponseMessage Reply(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Tiny mutable-string box mirroring the App's <c>LiveCurseForgeKey</c> holder. The
    /// repository reads the latest value through <see cref="Read"/>; the test mutates it
    /// via <see cref="Set"/> to mimic the VM persisting a fresh key.
    /// </summary>
    private sealed class MutableString
    {
        private string _value;
        public MutableString(string initial) => _value = initial ?? string.Empty;
        public string Read() => _value;
        public void Set(string newValue) => _value = newValue ?? string.Empty;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
