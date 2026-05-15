using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class InstalledVersionScannerTests : IDisposable
{
    private readonly string _temp;

    public InstalledVersionScannerTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-mc-scanner-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* best-effort */ }
    }

    private string MakeVersion(string id, string json)
    {
        var dir = Path.Combine(_temp, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{id}.json"), json);
        return dir;
    }

    [Fact]
    public void Scan_MissingDirectory_ReturnsEmpty()
    {
        var scanner = new FileSystemInstalledVersionScanner();
        var result = scanner.Scan(Path.Combine(_temp, "does-not-exist"));
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_EmptyDirectory_ReturnsEmpty()
    {
        var scanner = new FileSystemInstalledVersionScanner();
        var result = scanner.Scan(_temp);
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_SingleVanillaRelease_ParsesCoreFields()
    {
        MakeVersion("1.21.5", """
            {
              "id": "1.21.5",
              "type": "release",
              "releaseTime": "2026-03-25T13:00:00+00:00",
              "mainClass": "net.minecraft.client.main.Main"
            }
            """);
        // jar present, so JarPath should be populated.
        File.WriteAllBytes(Path.Combine(_temp, "1.21.5", "1.21.5.jar"), new byte[] { 0x50, 0x4B });

        var result = new FileSystemInstalledVersionScanner().Scan(_temp);

        var v = Assert.Single(result);
        Assert.Equal("1.21.5", v.Id);
        Assert.Equal("release", v.Type);
        Assert.Equal(new DateTimeOffset(2026, 3, 25, 13, 0, 0, TimeSpan.Zero), v.ReleaseTime);
        Assert.Equal(ModLoader.None, v.Loader);
        Assert.Null(v.ParentVersionId);
        Assert.NotNull(v.JarPath);
    }

    [Fact]
    public void Scan_ForgeInstall_DetectsForgeLoaderAndParent()
    {
        MakeVersion("1.20.1-forge-47.4.5", """
            {
              "id": "1.20.1-forge-47.4.5",
              "type": "release",
              "inheritsFrom": "1.20.1",
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher"
            }
            """);

        var v = Assert.Single(new FileSystemInstalledVersionScanner().Scan(_temp));

        Assert.Equal(ModLoader.Forge, v.Loader);
        Assert.Equal("1.20.1", v.ParentVersionId);
        Assert.Null(v.JarPath);
    }

    [Fact]
    public void Scan_NeoForgeInstall_DetectsNeoForge()
    {
        MakeVersion("1.20.4-neoforge-20.4.234", """
            {
              "id": "1.20.4-neoforge-20.4.234",
              "type": "release",
              "inheritsFrom": "1.20.4",
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher"
            }
            """);

        var v = Assert.Single(new FileSystemInstalledVersionScanner().Scan(_temp));

        Assert.Equal(ModLoader.NeoForge, v.Loader);
        Assert.Equal("1.20.4", v.ParentVersionId);
    }

    [Fact]
    public void Scan_FabricInstall_DetectsFabric()
    {
        MakeVersion("fabric-loader-0.15.11-1.20.4", """
            {
              "id": "fabric-loader-0.15.11-1.20.4",
              "type": "release",
              "inheritsFrom": "1.20.4",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient"
            }
            """);

        var v = Assert.Single(new FileSystemInstalledVersionScanner().Scan(_temp));

        Assert.Equal(ModLoader.Fabric, v.Loader);
    }

    [Fact]
    public void Scan_QuiltInstall_DetectsQuilt()
    {
        MakeVersion("quilt-loader-0.26.0-1.20.4", """
            {
              "id": "quilt-loader-0.26.0-1.20.4",
              "type": "release",
              "inheritsFrom": "1.20.4",
              "mainClass": "org.quiltmc.loader.impl.launch.knot.KnotClient"
            }
            """);

        var v = Assert.Single(new FileSystemInstalledVersionScanner().Scan(_temp));

        Assert.Equal(ModLoader.Quilt, v.Loader);
    }

    [Fact]
    public void Scan_MalformedJson_IsSkippedSilently()
    {
        MakeVersion("broken", "{ not valid json");
        MakeVersion("1.21.5", """{ "id": "1.21.5", "type": "release" }""");

        var result = new FileSystemInstalledVersionScanner().Scan(_temp);

        var v = Assert.Single(result);
        Assert.Equal("1.21.5", v.Id);
    }

    [Fact]
    public void Scan_DirectoryWithoutMatchingJson_IsSkipped()
    {
        // A version folder must contain "<id>.json". A bare folder is ignored.
        Directory.CreateDirectory(Path.Combine(_temp, "stray-folder"));
        MakeVersion("1.21.5", """{ "id": "1.21.5", "type": "release" }""");

        var result = new FileSystemInstalledVersionScanner().Scan(_temp);

        Assert.Single(result);
    }

    [Fact]
    public void Scan_OrdersByReleaseTimeDescending_FallingBackToIdDescending()
    {
        MakeVersion("1.21.5", """{ "id": "1.21.5", "type": "release", "releaseTime": "2026-03-25T13:00:00+00:00" }""");
        MakeVersion("1.20.1", """{ "id": "1.20.1", "type": "release", "releaseTime": "2024-06-07T13:00:00+00:00" }""");
        MakeVersion("custom-snapshot", """{ "id": "custom-snapshot", "type": "snapshot" }""");

        var ids = new FileSystemInstalledVersionScanner().Scan(_temp).Select(v => v.Id).ToArray();

        // Two with releaseTime come first, newest-first; the timeless one goes last.
        Assert.Equal(new[] { "1.21.5", "1.20.1", "custom-snapshot" }, ids);
    }

    [Fact]
    public async Task ListInstalledVersionsAsync_DelegatesToScannerAndLogs()
    {
        MakeVersion("1.21.5", """{ "id": "1.21.5", "type": "release", "releaseTime": "2026-03-25T00:00:00Z" }""");
        var logger = new RecordingLogger();
        var locator = new FixedInstallationLocator(_temp);
        var service = new CmlLibMinecraftLauncherService(
            new FakeUnderlyingLauncher(), logger,
            microsoftAuth: null,
            installedScanner: new FileSystemInstalledVersionScanner(),
            installationLocator: locator);

        var result = await service.ListInstalledVersionsAsync(CancellationToken.None);

        var v = Assert.Single(result);
        Assert.Equal("1.21.5", v.Id);
        Assert.Contains(logger.InfoEntries, e => e.Contains("Found 1"));
    }

    [Fact]
    public async Task ListInstalledVersionsAsync_MissingVersionsFolder_ReturnsEmpty()
    {
        var locator = new FixedInstallationLocator(Path.Combine(_temp, "nope"));
        var service = new CmlLibMinecraftLauncherService(
            new FakeUnderlyingLauncher(), new RecordingLogger(),
            microsoftAuth: null,
            installedScanner: new FileSystemInstalledVersionScanner(),
            installationLocator: locator);

        var result = await service.ListInstalledVersionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}

internal sealed class FixedInstallationLocator : IMinecraftInstallationLocator
{
    private readonly string _root;
    public FixedInstallationLocator(string versionsFolderAsRoot)
    {
        // Tests pass a directory that already holds <id>/<id>.json structures - i.e. it represents
        // the inner "versions/" folder. So we point .VersionsDirectory at it directly, and set Root
        // to its parent (which is rarely inspected by the production code path under test).
        _root = Path.GetDirectoryName(versionsFolderAsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? versionsFolderAsRoot;
        VersionsDirectory = versionsFolderAsRoot;
    }
    public string VersionsDirectory { get; }
    public MinecraftInstallation Locate() => new()
    {
        Root = _root,
        VersionsDirectory = VersionsDirectory,
        LauncherProfilesPath = Path.Combine(_root, "launcher_profiles.json"),
        LauncherAccountsPath = Path.Combine(_root, "launcher_accounts.json"),
        ServersDatPath = Path.Combine(_root, "servers.dat"),
    };
}
