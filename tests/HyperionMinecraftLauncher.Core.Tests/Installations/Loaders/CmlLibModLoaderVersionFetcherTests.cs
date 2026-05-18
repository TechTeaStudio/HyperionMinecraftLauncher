using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Installations.Loaders;

public class CmlLibModLoaderVersionFetcherTests
{
    [Fact]
    public async Task ListLoaderVersionsAsync_None_ReturnsEmpty_WithoutTouchingAnyFetcher()
    {
        var forge = new StubForgeVersionFetcher();
        var sut = new CmlLibModLoaderVersionFetcher(forge: forge);

        var result = await sut.ListLoaderVersionsAsync(ModLoader.None, "1.21.5", CancellationToken.None);

        Assert.Empty(result);
        Assert.False(forge.WasCalled);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_Forge_DispatchesAndReturnsInOrder()
    {
        var forge = new StubForgeVersionFetcher { Result = new[] { "49.0.30", "49.0.29", "47.4.5" } };
        var sut = new CmlLibModLoaderVersionFetcher(forge: forge);

        var result = await sut.ListLoaderVersionsAsync(ModLoader.Forge, "1.20.4", CancellationToken.None);

        Assert.Equal(new[] { "49.0.30", "49.0.29", "47.4.5" }, result);
        Assert.True(forge.WasCalled);
        Assert.Equal("1.20.4", forge.LastMcVersion);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_NeoForge_Dispatches()
    {
        var neo = new StubNeoForgeVersionFetcher { Result = new[] { "21.1.50" } };
        var sut = new CmlLibModLoaderVersionFetcher(neoForge: neo);

        var result = await sut.ListLoaderVersionsAsync(ModLoader.NeoForge, "1.21.1", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("21.1.50", result[0]);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_Fabric_Dispatches()
    {
        var fabric = new StubFabricVersionFetcher { Result = new[] { "0.16.10", "0.16.9" } };
        var sut = new CmlLibModLoaderVersionFetcher(fabric: fabric);

        var result = await sut.ListLoaderVersionsAsync(ModLoader.Fabric, "1.21.5", CancellationToken.None);

        Assert.Equal(new[] { "0.16.10", "0.16.9" }, result);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_Quilt_Dispatches()
    {
        var quilt = new StubQuiltVersionFetcher { Result = new[] { "0.26.0", "0.25.0-beta.4" } };
        var sut = new CmlLibModLoaderVersionFetcher(quilt: quilt);

        var result = await sut.ListLoaderVersionsAsync(ModLoader.Quilt, "1.21.5", CancellationToken.None);

        Assert.Equal(new[] { "0.26.0", "0.25.0-beta.4" }, result);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_UnconfiguredLoader_ReturnsEmpty()
    {
        var sut = new CmlLibModLoaderVersionFetcher();
        var result = await sut.ListLoaderVersionsAsync(ModLoader.Forge, "1.20.4", CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_EmptyMcVersion_Throws()
    {
        var sut = new CmlLibModLoaderVersionFetcher(forge: new StubForgeVersionFetcher());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ListLoaderVersionsAsync(ModLoader.Forge, "", CancellationToken.None));
    }

    [Fact]
    public async Task ListLoaderVersionsAsync_OptiFineOrOther_ReturnsEmpty()
    {
        var sut = new CmlLibModLoaderVersionFetcher(forge: new StubForgeVersionFetcher { Result = new[] { "x" } });

        Assert.Empty(await sut.ListLoaderVersionsAsync(ModLoader.OptiFine, "1.20.4", CancellationToken.None));
        Assert.Empty(await sut.ListLoaderVersionsAsync(ModLoader.LegacyForge, "1.7.10", CancellationToken.None));
        Assert.Empty(await sut.ListLoaderVersionsAsync(ModLoader.Other, "1.20.4", CancellationToken.None));
    }
}

internal sealed class StubForgeVersionFetcher : IUnderlyingForgeVersionFetcher
{
    public IReadOnlyList<string> Result { get; set; } = Array.Empty<string>();
    public bool WasCalled { get; private set; }
    public string? LastMcVersion { get; private set; }
    public Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        WasCalled = true;
        LastMcVersion = minecraftVersion;
        return Task.FromResult(Result);
    }
}

internal sealed class StubNeoForgeVersionFetcher : IUnderlyingNeoForgeVersionFetcher
{
    public IReadOnlyList<string> Result { get; set; } = Array.Empty<string>();
    public Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
        => Task.FromResult(Result);
}

internal sealed class StubFabricVersionFetcher : IUnderlyingFabricVersionFetcher
{
    public IReadOnlyList<string> Result { get; set; } = Array.Empty<string>();
    public Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
        => Task.FromResult(Result);
}

internal sealed class StubQuiltVersionFetcher : IUnderlyingQuiltVersionFetcher
{
    public IReadOnlyList<string> Result { get; set; } = Array.Empty<string>();
    public Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
        => Task.FromResult(Result);
}
