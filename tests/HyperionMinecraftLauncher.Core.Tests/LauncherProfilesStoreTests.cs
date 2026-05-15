using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

public class LauncherProfilesStoreTests : IDisposable
{
    private readonly string _temp;
    private readonly string _path;

    public LauncherProfilesStoreTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "hyperion-profiles-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_temp);
        _path = Path.Combine(_temp, "launcher_profiles.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public async Task LoadAsync_FileMissing_ReturnsEmpty()
    {
        var store = new FileLauncherProfilesStore();
        var result = await store.LoadAsync(_path, CancellationToken.None);
        Assert.Empty(result.Profiles);
        Assert.Equal(0, result.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_RealisticModernFile_ParsesAllFields()
    {
        File.WriteAllText(_path, """
        {
          "profiles": {
            "76a9b3a8-25e2-4f3e-bf48-1d3e2c9c2a11": {
              "name": "1.20.4",
              "type": "custom",
              "created": "2026-03-14T09:21:07.412Z",
              "lastUsed": "2026-05-12T20:08:55.901Z",
              "lastVersionId": "1.20.4",
              "icon": "Furnace",
              "gameDir": "C:\\Users\\iaroslav\\AppData\\Roaming\\.minecraft",
              "javaArgs": "-Xmx4G -XX:+UseG1GC",
              "javaDir": "C:\\Program Files\\Java\\jdk-21\\bin\\javaw.exe",
              "resolution": { "width": 1280, "height": 720 }
            },
            "latest-release": {
              "name": "",
              "type": "latest-release",
              "icon": "Grass",
              "lastVersionId": "latest-release"
            }
          },
          "settings": { "enableSnapshots": false },
          "version": 3,
          "clientToken": "irrelevant"
        }
        """);

        var result = await new FileLauncherProfilesStore().LoadAsync(_path, CancellationToken.None);

        Assert.Equal(3, result.SchemaVersion);
        Assert.Equal(2, result.Profiles.Count);

        var custom = result.Profiles.First(p => p.Type == "custom");
        Assert.Equal("76a9b3a8-25e2-4f3e-bf48-1d3e2c9c2a11", custom.Key);
        Assert.Equal("1.20.4", custom.Name);
        Assert.Equal("1.20.4", custom.LastVersionId);
        Assert.Equal("Furnace", custom.Icon);
        Assert.Equal("-Xmx4G -XX:+UseG1GC", custom.JavaArgs);
        Assert.Equal(1280, custom.ResolutionWidth);
        Assert.Equal(720, custom.ResolutionHeight);
        Assert.NotNull(custom.Created);
        Assert.NotNull(custom.LastUsed);

        var release = result.Profiles.First(p => p.Type == "latest-release");
        Assert.Equal(string.Empty, release.Name);
        Assert.Equal("latest-release", release.LastVersionId);
        Assert.Null(release.ResolutionWidth);
    }

    [Fact]
    public async Task LoadAsync_NoProfilesKey_ReturnsEmpty()
    {
        File.WriteAllText(_path, """{ "settings": {}, "version": 3 }""");

        var result = await new FileLauncherProfilesStore().LoadAsync(_path, CancellationToken.None);

        Assert.Empty(result.Profiles);
        Assert.Equal(3, result.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_MissingOptionalFields_DefaultsAreSensible()
    {
        File.WriteAllText(_path, """
        {
          "profiles": {
            "minimal": { "name": "Bare", "type": "custom" }
          },
          "version": 3
        }
        """);

        var result = await new FileLauncherProfilesStore().LoadAsync(_path, CancellationToken.None);

        var p = Assert.Single(result.Profiles);
        Assert.Equal("Bare", p.Name);
        Assert.Null(p.Created);
        Assert.Null(p.LastUsed);
        Assert.Null(p.GameDir);
        Assert.Null(p.JavaArgs);
        Assert.Null(p.JavaDir);
        Assert.Null(p.LastVersionId);
        Assert.Null(p.ResolutionWidth);
        Assert.Null(p.ResolutionHeight);
    }
}
