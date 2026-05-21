using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class LauncherSettingsStoreTests : IDisposable
{
    private readonly string _temp;
    private readonly string _path;

    public LauncherSettingsStoreTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-settings-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
        _path = Path.Combine(_temp, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaults()
    {
        var store = new FileLauncherSettingsStore(_path);
        var s = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1024, s.MinimumRamMb);
        Assert.Equal(4096, s.MaximumRamMb);
        Assert.True(s.KeepLauncherOpen);
        Assert.False(s.ShowGameLog);
        Assert.False(s.SidebarCollapsed);
        Assert.Null(s.GameDirectory);
        Assert.Null(s.JavaExecutable);
        Assert.Equal(string.Empty, s.JvmArguments);
        Assert.True(s.AutoBackupBeforeLaunch);
        Assert.Equal(5, s.AutoBackupKeepLatest);
        // ForceOfflineMode is a dev/QA flag - it must default OFF so a stock launcher
        // never accidentally bypasses Microsoft auth.
        Assert.False(s.ForceOfflineMode);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryField()
    {
        var store = new FileLauncherSettingsStore(_path);
        var original = new LauncherSettings
        {
            MinimumRamMb = 2048,
            MaximumRamMb = 8192,
            JvmArguments = "-XX:+UseG1GC -XX:MaxGCPauseMillis=50",
            GameDirectory = @"C:\Custom\.minecraft",
            JavaExecutable = @"C:\Java\jdk-21\bin\javaw.exe",
            KeepLauncherOpen = false,
            ShowGameLog = true,
            SidebarCollapsed = true,
            AutoBackupBeforeLaunch = false,
            AutoBackupKeepLatest = 12,
            ForceOfflineMode = true,
        };
        await store.SaveAsync(original, CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(original, loaded);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsForceOfflineModeBothStates()
    {
        // Dedicated round-trip for the dev-flag because it has no UI surface in older builds:
        // catching a regression where the JSON property is renamed / dropped is easier with
        // a focused assert than picking it out of the omnibus test above.
        var store = new FileLauncherSettingsStore(_path);

        await store.SaveAsync(new LauncherSettings { ForceOfflineMode = true }, CancellationToken.None);
        var loadedOn = await store.LoadAsync(CancellationToken.None);
        Assert.True(loadedOn.ForceOfflineMode);

        await store.SaveAsync(new LauncherSettings { ForceOfflineMode = false }, CancellationToken.None);
        var loadedOff = await store.LoadAsync(CancellationToken.None);
        Assert.False(loadedOff.ForceOfflineMode);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(_path, "{ not valid json");
        var store = new FileLauncherSettingsStore(_path);

        var s = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1024, s.MinimumRamMb);
    }

    [Fact]
    public async Task SaveAsync_CreatesParentDirectory()
    {
        var deep = Path.Combine(_temp, "a", "b", "c", "settings.json");
        var store = new FileLauncherSettingsStore(deep);

        await store.SaveAsync(new LauncherSettings(), CancellationToken.None);

        Assert.True(File.Exists(deep));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSidebarCollapsedBothStates()
    {
        var store = new FileLauncherSettingsStore(_path);

        await store.SaveAsync(new LauncherSettings { SidebarCollapsed = true }, CancellationToken.None);
        var loadedCollapsed = await store.LoadAsync(CancellationToken.None);
        Assert.True(loadedCollapsed.SidebarCollapsed);

        await store.SaveAsync(new LauncherSettings { SidebarCollapsed = false }, CancellationToken.None);
        var loadedExpanded = await store.LoadAsync(CancellationToken.None);
        Assert.False(loadedExpanded.SidebarCollapsed);
    }
}
