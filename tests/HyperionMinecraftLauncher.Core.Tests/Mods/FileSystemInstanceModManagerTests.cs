using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Mods;

public class FileSystemInstanceModManagerTests : IDisposable
{
    private readonly string _tempRoot;

    public FileSystemInstanceModManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hyperion-mod-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private Instance NewInstance() => new()
    {
        Id = "abc",
        Name = "Test",
        VersionId = "1.20.1",
        GameDirectory = _tempRoot,
    };

    [Fact]
    public async Task InstallAsync_CopiesBytesIntoModsFolder()
    {
        var payload = Encoding.UTF8.GetBytes("mod-bytes-here");
        var handler = new ByteHandler(payload);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var repo = new ModrinthRepository(http);
        var mgr = new FileSystemInstanceModManager();

        var inst = NewInstance();
        var file = new ModFile
        {
            ModId = "X", FileId = "1", DisplayName = "1.0",
            Filename = "cool-mod.jar",
            DownloadUrl = "https://example.invalid/cool-mod.jar",
            FileSize = payload.Length,
        };

        await mgr.InstallAsync(inst, file, repo, CancellationToken.None);

        var written = Path.Combine(_tempRoot, "mods", "cool-mod.jar");
        Assert.True(File.Exists(written));
        Assert.Equal(payload, File.ReadAllBytes(written));
    }

    [Fact]
    public async Task ListInstalled_EnumeratesJarAndDisabled()
    {
        var modsDir = Path.Combine(_tempRoot, "mods");
        Directory.CreateDirectory(modsDir);
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "alpha.jar"), new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "beta.jar.disabled"), new byte[] { 4, 5, 6, 7 });

        var mgr = new FileSystemInstanceModManager();

        var list = await mgr.ListInstalledAsync(NewInstance(), CancellationToken.None);

        Assert.Equal(2, list.Count);
        var alpha = Assert.Single(list, m => m.Filename == "alpha.jar");
        var beta = Assert.Single(list, m => m.Filename == "beta.jar.disabled");
        Assert.True(alpha.IsEnabled);
        Assert.False(beta.IsEnabled);
        Assert.Equal(3, alpha.SizeBytes);
        Assert.Equal(4, beta.SizeBytes);
    }

    [Fact]
    public async Task SetEnabledAsync_RenamesAtomicallyBothWays()
    {
        var modsDir = Path.Combine(_tempRoot, "mods");
        Directory.CreateDirectory(modsDir);
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "toggle.jar"), new byte[] { 9 });

        var mgr = new FileSystemInstanceModManager();
        var inst = NewInstance();

        await mgr.SetEnabledAsync(inst, "toggle.jar", enabled: false, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(modsDir, "toggle.jar")));
        Assert.True(File.Exists(Path.Combine(modsDir, "toggle.jar.disabled")));

        await mgr.SetEnabledAsync(inst, "toggle.jar.disabled", enabled: true, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(modsDir, "toggle.jar")));
        Assert.False(File.Exists(Path.Combine(modsDir, "toggle.jar.disabled")));
    }

    [Fact]
    public async Task RemoveAsync_DeletesFileByEitherName()
    {
        var modsDir = Path.Combine(_tempRoot, "mods");
        Directory.CreateDirectory(modsDir);
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "gone.jar.disabled"), new byte[] { 1 });

        var mgr = new FileSystemInstanceModManager();
        await mgr.RemoveAsync(NewInstance(), "gone.jar.disabled", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(modsDir, "gone.jar.disabled")));
    }

    [Fact]
    public async Task ListInstalled_NoFolderReturnsEmpty()
    {
        var mgr = new FileSystemInstanceModManager();
        var list = await mgr.ListInstalledAsync(NewInstance(), CancellationToken.None);
        Assert.Empty(list);
    }

    private sealed class ByteHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        public ByteHandler(byte[] payload) => _payload = payload;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
            });
    }
}
