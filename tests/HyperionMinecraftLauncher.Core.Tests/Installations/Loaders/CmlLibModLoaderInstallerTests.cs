using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Installations.Loaders;

public class CmlLibModLoaderInstallerTests
{
    [Fact]
    public async Task InstallAsync_None_ReturnsVanillaIdUnchanged_WithoutTouchingAnyUnderlying()
    {
        var forge = new RecordingForge();
        var fabric = new RecordingFabric();
        var quilt = new RecordingQuilt();
        var neo = new RecordingNeoForge();
        var sut = new CmlLibModLoaderInstaller(forge, neo, fabric, quilt);

        var result = await sut.InstallAsync(ModLoader.None, "1.21.5", null, null, CancellationToken.None);

        Assert.Equal("1.21.5", result);
        Assert.False(forge.WasCalled);
        Assert.False(fabric.WasCalled);
        Assert.False(quilt.WasCalled);
        Assert.False(neo.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_Forge_DispatchesToForgeUnderlyingAndReturnsItsResult()
    {
        var forge = new RecordingForge { ReturnValue = "1.20.4-forge-49.0.30" };
        var sut = new CmlLibModLoaderInstaller(forge: forge);

        var result = await sut.InstallAsync(ModLoader.Forge, "1.20.4", "49.0.30", null, CancellationToken.None);

        Assert.Equal("1.20.4-forge-49.0.30", result);
        Assert.True(forge.WasCalled);
        Assert.Equal("1.20.4", forge.LastMcVersion);
        Assert.Equal("49.0.30", forge.LastLoaderVersion);
    }

    [Fact]
    public async Task InstallAsync_Forge_NullLoaderVersion_PropagatesNull()
    {
        var forge = new RecordingForge { ReturnValue = "1.20.4-forge-latest" };
        var sut = new CmlLibModLoaderInstaller(forge: forge);

        var result = await sut.InstallAsync(ModLoader.Forge, "1.20.4", null, null, CancellationToken.None);

        Assert.Equal("1.20.4-forge-latest", result);
        Assert.True(forge.WasCalled);
        Assert.Null(forge.LastLoaderVersion);
    }

    [Fact]
    public async Task InstallAsync_NeoForge_Dispatches()
    {
        var neo = new RecordingNeoForge { ReturnValue = "neoforge-21.1.50" };
        var sut = new CmlLibModLoaderInstaller(neoForge: neo);

        var result = await sut.InstallAsync(ModLoader.NeoForge, "1.21.1", "21.1.50", null, CancellationToken.None);

        Assert.Equal("neoforge-21.1.50", result);
        Assert.True(neo.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_Fabric_Dispatches()
    {
        var fabric = new RecordingFabric { ReturnValue = "fabric-loader-0.16.10-1.21.5" };
        var sut = new CmlLibModLoaderInstaller(fabric: fabric);

        var result = await sut.InstallAsync(ModLoader.Fabric, "1.21.5", "0.16.10", null, CancellationToken.None);

        Assert.Equal("fabric-loader-0.16.10-1.21.5", result);
        Assert.True(fabric.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_Quilt_Dispatches()
    {
        var quilt = new RecordingQuilt { ReturnValue = "quilt-loader-0.26.0-1.21.5" };
        var sut = new CmlLibModLoaderInstaller(quilt: quilt);

        var result = await sut.InstallAsync(ModLoader.Quilt, "1.21.5", "0.26.0", null, CancellationToken.None);

        Assert.Equal("quilt-loader-0.26.0-1.21.5", result);
        Assert.True(quilt.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_Forge_WithNoUnderlying_Throws()
    {
        var sut = new CmlLibModLoaderInstaller();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.Forge, "1.20.4", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_Fabric_WithNoUnderlying_Throws()
    {
        var sut = new CmlLibModLoaderInstaller();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.Fabric, "1.21.5", "0.16.10", null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_NeoForge_WithNoUnderlying_Throws()
    {
        var sut = new CmlLibModLoaderInstaller();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.NeoForge, "1.21.1", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_Quilt_WithNoUnderlying_Throws()
    {
        var sut = new CmlLibModLoaderInstaller();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.Quilt, "1.21.5", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_OptiFine_AlwaysThrowsNotSupported()
    {
        var sut = new CmlLibModLoaderInstaller(
            forge: new RecordingForge(), neoForge: new RecordingNeoForge(),
            fabric: new RecordingFabric(), quilt: new RecordingQuilt());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.OptiFine, "1.20.4", null, null, CancellationToken.None));
        Assert.Contains("OptiFine", ex.Message);
    }

    [Fact]
    public async Task InstallAsync_LegacyForge_AlwaysThrowsNotSupported()
    {
        var sut = new CmlLibModLoaderInstaller(forge: new RecordingForge());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.InstallAsync(ModLoader.LegacyForge, "1.7.10", null, null, CancellationToken.None));
        Assert.Contains("Legacy Forge", ex.Message);
    }

    [Fact]
    public async Task InstallAsync_EmptyMcVersion_Throws()
    {
        var sut = new CmlLibModLoaderInstaller(fabric: new RecordingFabric());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.InstallAsync(ModLoader.Fabric, "", "0.16.10", null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_ForwardsProgressAndToken()
    {
        var forge = new RecordingForge { ReturnValue = "1.20.4-forge-49.0.30" };
        var sut = new CmlLibModLoaderInstaller(forge: forge);
        using var cts = new CancellationTokenSource();
        var fakeProgress = new RecordingProgress();

        _ = await sut.InstallAsync(ModLoader.Forge, "1.20.4", "49.0.30", fakeProgress, cts.Token);

        Assert.Same(fakeProgress, forge.LastProgress);
        Assert.Equal(cts.Token, forge.LastToken);
    }
}

internal sealed class RecordingProgress : IProgress<double>
{
    public List<double> Reports { get; } = new();
    public void Report(double value) => Reports.Add(value);
}

internal sealed class RecordingForge : IUnderlyingForgeInstaller
{
    public string ReturnValue { get; set; } = "<forge-result>";
    public bool WasCalled { get; private set; }
    public string? LastMcVersion { get; private set; }
    public string? LastLoaderVersion { get; private set; }
    public IProgress<double>? LastProgress { get; private set; }
    public CancellationToken LastToken { get; private set; }

    public Task<string> InstallAsync(string minecraftVersion, string? forgeVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        WasCalled = true;
        LastMcVersion = minecraftVersion;
        LastLoaderVersion = forgeVersion;
        LastProgress = progress;
        LastToken = cancellationToken;
        return Task.FromResult(ReturnValue);
    }
}

internal sealed class RecordingNeoForge : IUnderlyingNeoForgeInstaller
{
    public string ReturnValue { get; set; } = "<neoforge-result>";
    public bool WasCalled { get; private set; }
    public Task<string> InstallAsync(string minecraftVersion, string? neoForgeVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        WasCalled = true;
        return Task.FromResult(ReturnValue);
    }
}

internal sealed class RecordingFabric : IUnderlyingFabricInstaller
{
    public string ReturnValue { get; set; } = "<fabric-result>";
    public bool WasCalled { get; private set; }
    public Task<string> InstallAsync(string minecraftVersion, string? fabricLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        WasCalled = true;
        return Task.FromResult(ReturnValue);
    }
}

internal sealed class RecordingQuilt : IUnderlyingQuiltInstaller
{
    public string ReturnValue { get; set; } = "<quilt-result>";
    public bool WasCalled { get; private set; }
    public Task<string> InstallAsync(string minecraftVersion, string? quiltLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        WasCalled = true;
        return Task.FromResult(ReturnValue);
    }
}
