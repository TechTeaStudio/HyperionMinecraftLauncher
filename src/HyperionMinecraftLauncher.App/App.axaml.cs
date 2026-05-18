using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.App.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

namespace TechTeaStudio.HyperionMinecraftLauncher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // T18: wall-clock timeline begins at the Avalonia "framework-init-completed" boundary so
        // the "dispatcher" delta below reflects the time we spent wiring services + the first
        // window. Subsequent checkpoints land in MainViewModel.RunStartupRefreshesAsync.
        StartupTimeline.Begin();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Manual constructor DI - no container. Mirrors the HhStoryGenerator pilot's wiring.
            // v0.28.0: swapped from FileLauncherLogger to SerilogLauncherLogger for production
            // logging. FileLauncherLogger is kept (marked [Obsolete]) for deterministic tests
            // that need a clock-injected file logger.
            var logger = new SerilogLauncherLogger(DefaultLogDirectory.Resolve());
            logger.Info($"HyperionMinecraftLauncher 0.28.0 starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

            // Shared account store: the v0.27.0 multi-account roster lives next to MSAL's own
            // refresh-token cache. The Microsoft auth service reads/writes it on every sign-in;
            // the view-model surfaces it through the header chip flyout.
            var accountStore = new FileAccountStore();
            var microsoftAuth = new MicrosoftAuthService(logger, DefaultMsalAccountCachePath(), accountStore);
            var settingsStore = new FileLauncherSettingsStore();

            // Shared disk cache for news, player skins, and anything else network-bound.
            // News re-fetches at most once per hour; skins for 6 h; stale entries are also served
            // on network failure so the launcher stays usable offline.
            var cache = new FileCache();
            var httpClient = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
            var newsClient = new MojangNewsClient(httpClient, cache);

            // Live server-status pings over TCP - one round-trip per row on the Servers page.
            // The default 1.8 protocol number keeps us compatible with virtually every modern server.
            var serverPinger = new TcpServerPinger();

            // Adoptium Temurin auto-installer: writes JREs under %LOCALAPPDATA%/HyperionMinecraftLauncher/java/.
            // Reuses its own HttpClient with a generous 5-minute timeout because a fresh JRE download
            // on a slow link is the longest-running thing the launcher pulls.
            var javaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var javaRuntimeManager = new AdoptiumJavaRuntimeManager(
                javaHttp,
                AdoptiumJavaRuntimeManager.DefaultRootDirectory(),
                AdoptiumJavaRuntimeManager.DetectOs(),
                AdoptiumJavaRuntimeManager.DetectArch());

            var service = new CmlLibMinecraftLauncherService(
                new CmlLibUnderlyingLauncher(), logger, microsoftAuth,
                newsClient: newsClient,
                serverPinger: serverPinger,
                javaRuntimeManager: javaRuntimeManager);

            // Discord Rich Presence - obeys the LauncherSettings toggle. Read settings synchronously
            // here for the same reason MainViewModel does: the file is tiny and the wiring has to know
            // the flag before constructing the VM. Disabled / load-failed both fall back to no-op so
            // launcher startup never depends on Discord being installed.
            var initialSettings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            IPresenceService presence;
            try
            {
                presence = initialSettings.DiscordRpcEnabled
                    ? new DiscordPresenceService(logger)
                    : new NullPresenceService();
            }
            catch (System.Exception ex)
            {
                logger.Warn($"Could not init Discord presence ({ex.Message}); falling back to no-op.");
                presence = new NullPresenceService();
            }
            // Per-instance browser: lists screenshots / worlds / servers under each instance's gameDir.
            var instanceBrowser = new FileSystemInstanceBrowser();

            // Per-instance crash report listener (v0.30 T21c): parses crash-reports/*.txt and
            // ranks suspect mods by stack-trace frame. Wraps the built-in MinecraftCrashReportParser.
            var crashReportListener = new FileSystemCrashReportListener();

            // Skin upload + cape + history services. Reuse the launcher's shared HttpClient.
            var skinService = new MojangSkinService(httpClient);
            var skinHistoryDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HyperionMinecraftLauncher", "skins_history");
            var skinHistory = new FileSkinHistoryStore(skinHistoryDir);

            // Mod repositories: Modrinth always-on (no key needed); CurseForge inert until
            // the user pastes a key into Settings. Both share their own HttpClient with a
            // 20 s timeout so big project pages don't hang the UI.
            var modsHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var modrinthRepo = new ModrinthRepository(modsHttp);
            var curseForgeHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var curseForgeRepo = new CurseForgeRepository(curseForgeHttp, initialSettings.CurseForgeApiKey, logger);
            var instanceModManager = new FileSystemInstanceModManager();

            // Update checker: shares the launcher's HttpClient + disk cache so the GitHub
            // releases probe is at most a once-per-hour round-trip on startup, and never
            // throws on failure (banner just stays hidden).
            var updateChecker = new GitHubReleasesUpdateChecker(httpClient, cache);
            // Headless dedicated-server registry (v0.28 T11). Folder-per-server under LOCALAPPDATA.
            var headlessServerStore = new FileHeadlessServerStore();

            // Per-instance world backup service (v0.30 T21f). Streams each saves/ subdir into
            // <gameDir>/backups/{worldName}-{ts}.zip before each launch, gated by
            // LauncherSettings.AutoBackupBeforeLaunch.
            var backupService = new FileSystemBackupService();

            var viewModel = new MainViewModel(
                service, logger, microsoftAuth, settingsStore,
                presence, instanceBrowser, skinService, skinHistory,
                modrinthRepo, curseForgeRepo, instanceModManager, accountStore,
                updateChecker, headlessServerStore, backupService, crashReportListener);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            // Cosmetic only: never let presence teardown errors escape into the close path.
            mainWindow.Closed += (_, _) =>
            {
                try { presence.Stop(); }
                catch (System.Exception ex) { logger.Warn($"Discord presence Stop on window close failed: {ex.Message}"); }
            };
            desktop.MainWindow = mainWindow;

            // Fire-and-forget: as soon as the dispatcher is idle after window construction,
            // populate Installed / Manifest / Profiles / Servers / News so the user doesn't
            // have to click five "Refresh" buttons before the launcher feels populated.
            // The "dispatcher" checkpoint captures wall-clock time from Begin() in T18.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () =>
                {
                    StartupTimeline.Mark("dispatcher");
                    _ = viewModel.RunStartupRefreshesAsync();
                },
                Avalonia.Threading.DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The legacy XboxAuthNet account-manager file path. Kept alongside the new
    /// accounts.v2.json so the migration in <see cref="FileAccountStore"/> can detect it.
    /// </summary>
    private static string DefaultMsalAccountCachePath()
    {
        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "HyperionMinecraftLauncher");
        return System.IO.Path.Combine(dir, "accounts.json");
    }
}
