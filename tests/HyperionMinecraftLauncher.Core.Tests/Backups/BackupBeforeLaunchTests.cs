using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Backups;

/// <summary>
/// LaunchCoreAsync must zip the instance's worlds via the injected backup service BEFORE
/// invoking the launcher service. Backup failure must NOT block the launch.
/// </summary>
public class BackupBeforeLaunchTests
{
    [Fact]
    public async Task LaunchInstance_WithAutoBackupOn_BacksUpBeforeLaunch()
    {
        var stubLauncher = new RecordingLauncherService();
        var backup = new RecordingBackupService();
        var browser = new RecordingBrowserService();
        // One world present -> the gate "instance has at least one world" passes.
        browser.WorldsByGameDir["g"] = new[]
        {
            new WorldEntry { FullPath = "g/saves/w", FolderName = "w", DisplayName = "w", SizeBytes = 0 },
        };

        var vm = new MainViewModel(
            service: stubLauncher,
            logger: new RecordingLogger(),
            instanceBrowser: browser,
            backupService: backup);

        var instance = new Instance { Id = "i", Name = "I", VersionId = "1.21.5", GameDirectory = "g" };
        await vm.QuickPlayLaunchAsync(instance, new QuickPlay.None());

        Assert.NotEmpty(backup.BackupCalls);
        Assert.Same(instance, backup.BackupCalls[0]);
        Assert.NotEmpty(backup.PruneCalls);
        Assert.Equal(5, backup.PruneCalls[0].keepLatest);

        // Backup must have happened BEFORE the launch (call ordering).
        Assert.True(backup.LastBackupSequence < stubLauncher.LaunchSequence,
            $"Backup seq={backup.LastBackupSequence} must precede launch seq={stubLauncher.LaunchSequence}");
        Assert.Contains("[backup]", vm.LogText);
    }

    [Fact]
    public async Task LaunchInstance_BackupFailure_StillProceeds()
    {
        var stubLauncher = new RecordingLauncherService();
        var browser = new RecordingBrowserService();
        browser.WorldsByGameDir["g"] = new[]
        {
            new WorldEntry { FullPath = "g/saves/w", FolderName = "w", DisplayName = "w", SizeBytes = 0 },
        };
        var backup = new RecordingBackupService { ThrowOnBackup = new IOException("disk full") };

        var vm = new MainViewModel(
            service: stubLauncher,
            logger: new RecordingLogger(),
            instanceBrowser: browser,
            backupService: backup);

        var instance = new Instance { Id = "i", Name = "I", VersionId = "1.21.5", GameDirectory = "g" };
        await vm.QuickPlayLaunchAsync(instance, new QuickPlay.None());

        Assert.True(stubLauncher.LaunchInvoked, "Launch must still proceed when backup fails.");
        Assert.Contains("[warn]", vm.LogText);
    }

    [Fact]
    public async Task LaunchInstance_NoWorlds_SkipsBackupQuietly()
    {
        var stubLauncher = new RecordingLauncherService();
        var browser = new RecordingBrowserService(); // no worlds for any gameDir
        var backup = new RecordingBackupService();

        var vm = new MainViewModel(
            service: stubLauncher,
            logger: new RecordingLogger(),
            instanceBrowser: browser,
            backupService: backup);

        var instance = new Instance { Id = "i", Name = "I", VersionId = "1.21.5", GameDirectory = "g" };
        await vm.QuickPlayLaunchAsync(instance, new QuickPlay.None());

        Assert.Empty(backup.BackupCalls);
        Assert.True(stubLauncher.LaunchInvoked);
    }

    [Fact]
    public async Task LaunchInstance_AutoBackupDisabled_DoesNotBackUp()
    {
        var stubLauncher = new RecordingLauncherService();
        var browser = new RecordingBrowserService();
        browser.WorldsByGameDir["g"] = new[]
        {
            new WorldEntry { FullPath = "g/saves/w", FolderName = "w", DisplayName = "w", SizeBytes = 0 },
        };
        var backup = new RecordingBackupService();

        var vm = new MainViewModel(
            service: stubLauncher,
            logger: new RecordingLogger(),
            instanceBrowser: browser,
            backupService: backup);
        vm.AutoBackupBeforeLaunch = false;

        var instance = new Instance { Id = "i", Name = "I", VersionId = "1.21.5", GameDirectory = "g" };
        await vm.QuickPlayLaunchAsync(instance, new QuickPlay.None());

        Assert.Empty(backup.BackupCalls);
        Assert.True(stubLauncher.LaunchInvoked);
    }

    // ---- stubs ----

    private static int s_sequence;

    private sealed class RecordingBackupService : IBackupService
    {
        public List<Instance> BackupCalls { get; } = new();
        public List<(Instance instance, int keepLatest)> PruneCalls { get; } = new();
        public int LastBackupSequence { get; private set; } = -1;
        public Exception? ThrowOnBackup { get; set; }

        public Task<IReadOnlyList<BackupEntry>> BackupAllWorldsAsync(Instance instance, CancellationToken cancellationToken)
        {
            LastBackupSequence = ++s_sequence;
            BackupCalls.Add(instance);
            if (ThrowOnBackup is not null) throw ThrowOnBackup;
            IReadOnlyList<BackupEntry> result = new[]
            {
                new BackupEntry
                {
                    ArchivePath = "g/backups/w-20260518-093000.zip",
                    CreatedAt = DateTimeOffset.UtcNow,
                    SizeBytes = 1024,
                    SourceWorldName = "w",
                },
            };
            return Task.FromResult(result);
        }

        public Task<BackupEntry?> BackupWorldAsync(Instance instance, string worldFolderName, CancellationToken cancellationToken)
            => Task.FromResult<BackupEntry?>(null);

        public Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BackupEntry>>(Array.Empty<BackupEntry>());

        public Task RestoreAsync(BackupEntry entry, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task PruneAsync(Instance instance, int keepLatest, CancellationToken cancellationToken)
        {
            PruneCalls.Add((instance, keepLatest));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBrowserService : IInstanceBrowser
    {
        public Dictionary<string, IReadOnlyList<WorldEntry>> WorldsByGameDir { get; } = new();

        public Task<IReadOnlyList<ScreenshotEntry>> ListScreenshotsAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ScreenshotEntry>>(Array.Empty<ScreenshotEntry>());

        public Task<IReadOnlyList<WorldEntry>> ListWorldsAsync(Instance instance, CancellationToken cancellationToken)
        {
            var key = instance.GameDirectory ?? string.Empty;
            return Task.FromResult(WorldsByGameDir.TryGetValue(key, out var w)
                ? w
                : (IReadOnlyList<WorldEntry>)Array.Empty<WorldEntry>());
        }

        public Task<IReadOnlyList<ServerListEntry>> ListServersAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ServerListEntry>>(Array.Empty<ServerListEntry>());

        public Task<IReadOnlyList<ResourcePackEntry>> ListResourcePacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ResourcePackEntry>>(Array.Empty<ResourcePackEntry>());

        public Task<IReadOnlyList<ShaderPackEntry>> ListShaderPacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ShaderPackEntry>>(Array.Empty<ShaderPackEntry>());

        public Task<IReadOnlyList<DataPackEntry>> ListDataPacksAsync(Instance instance, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DataPackEntry>>(Array.Empty<DataPackEntry>());
    }

    private sealed class RecordingLauncherService : IMinecraftLauncherService
    {
        public bool LaunchInvoked { get; private set; }
        public int LaunchSequence { get; private set; } = -1;

        public Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<VersionMetadata>>(Array.Empty<VersionMetadata>());

        public Task<IReadOnlyList<InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InstalledVersion>>(Array.Empty<InstalledVersion>());

        public Task<IReadOnlyList<LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LauncherProfile>>(Array.Empty<LauncherProfile>());

        public Task<IReadOnlyList<ServerListEntry>> ListServersAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ServerListEntry>>(Array.Empty<ServerListEntry>());

        public Task<IReadOnlyDictionary<string, ServerStatus?>> PingServersAsync(IEnumerable<ServerListEntry> entries, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, ServerStatus?>>(new Dictionary<string, ServerStatus?>());

        public Task<IReadOnlyList<NewsEntry>> ListNewsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NewsEntry>>(Array.Empty<NewsEntry>());

        public Task<IReadOnlyList<Instance>> ListInstancesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Instance>>(Array.Empty<Instance>());

        public Task SaveInstanceAsync(Instance instance, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteInstanceAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new AuthResult { Username = request.Username ?? "u", Uuid = "00", AccessToken = "", IsOffline = true });

        public Task<LaunchResult> LaunchAsync(LaunchRequest request, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        {
            LaunchInvoked = true;
            LaunchSequence = ++s_sequence;
            return Task.FromResult(new LaunchResult { ProcessId = 1, VersionName = request.VersionName });
        }
    }
}
