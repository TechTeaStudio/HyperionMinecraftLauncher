using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.App.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;
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
            var asmVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?.?.?";
            logger.Info($"HyperionMinecraftLauncher {asmVersion} starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

            // v0.32.2: SkinPreview lives in the App layer but has no constructor DI (it's
            // instantiated by AXAML). Static hook so its caught-exception paths can still
            // surface into the daily log file with the rest of the launcher diagnostics.
            TechTeaStudio.HyperionMinecraftLauncher.App.Controls.SkinPreview.Logger = (msg, ex) =>
            {
                if (ex is null) logger.Warn(msg);
                else logger.Error(msg, ex);
            };

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

            // The CmlLib MinecraftLauncher is shared by the underlying launcher (vanilla launches)
            // and the mod-loader install pipeline. Constructing it is cheap; constructing the
            // full mod-loader installer pipeline + Adoptium HttpClient is what we want off the
            // cold-start path -- so we keep the MinecraftLauncher eager and wrap the loader /
            // JRE pipeline in Lazy<> below.
            var minecraftLauncher = new CmlLib.Core.MinecraftLauncher();

            // v0.32.1 (T-startup-perf): the JRE manager and mod-loader install pipeline only
            // matter once the user actually presses Play. Lazy<> defers their HttpClient
            // construction (the JRE one carries a 5-minute timeout for big downloads) and the
            // four CmlLib underlying-installer wrappers until first-launch. .Value is thread-safe.
            var lazyJavaRuntimeManager = new Lazy<IJavaRuntimeManager>(
                () =>
                {
                    var javaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                    return new AdoptiumJavaRuntimeManager(
                        javaHttp,
                        AdoptiumJavaRuntimeManager.DefaultRootDirectory(),
                        AdoptiumJavaRuntimeManager.DetectOs(),
                        AdoptiumJavaRuntimeManager.DetectArch());
                },
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

            var lazyModLoaderInstaller = new Lazy<IModLoaderInstaller>(
                () => new CmlLibModLoaderInstaller(
                    forge: new CmlLibForgeUnderlying(minecraftLauncher),
                    neoForge: new CmlLibNeoForgeUnderlying(minecraftLauncher),
                    fabric: new CmlLibFabricUnderlying(httpClient, minecraftLauncher),
                    quilt: new CmlLibQuiltUnderlying(httpClient, minecraftLauncher)),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

            // Loader version fetcher is only used after the user picks a non-Vanilla loader on
            // the Settings page. Build eagerly -- four HttpClient-backed fetchers are cheap.
            var modLoaderVersionFetcher = new CmlLibModLoaderVersionFetcher(
                forge: new CmlLibForgeVersionFetcher(httpClient),
                neoForge: new CmlLibNeoForgeVersionFetcher(httpClient),
                fabric: new CmlLibFabricVersionFetcher(httpClient),
                quilt: new CmlLibQuiltVersionFetcher(httpClient));

            var service = new CmlLibMinecraftLauncherService(
                new CmlLibUnderlyingLauncher(minecraftLauncher), logger, microsoftAuth,
                newsClient: newsClient,
                serverPinger: serverPinger,
                versionManifestCache: cache,
                lazyJavaRuntimeManager: lazyJavaRuntimeManager,
                lazyModLoaderInstaller: lazyModLoaderInstaller);

            // v0.32.1 (T-startup-perf): the Discord IPC handshake can spend 100s of ms blocking the
            // dispatcher while it opens the named pipe. Hand the VM a DeferredPresenceService proxy
            // first so it can call SetIdle/SetPlaying immediately; the real DiscordPresenceService
            // is constructed on a background thread and attached when ready. Disabled / failed paths
            // attach a NullPresenceService so the proxy still forwards correctly.
            // The settings file is also read on the background thread now - the only flag the wiring
            // needs is DiscordRpcEnabled, and the VM does its own LoadAsync as part of InitializeAsync.
            var initialSettings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var deferredPresence = new DeferredPresenceService(logger);
            IPresenceService presence = deferredPresence;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                IPresenceService real;
                try
                {
                    real = initialSettings.DiscordRpcEnabled
                        ? new DiscordPresenceService(logger)
                        : new NullPresenceService();
                }
                catch (System.Exception ex)
                {
                    logger.Warn($"Could not init Discord presence ({ex.Message}); falling back to no-op.");
                    real = new NullPresenceService();
                }
                deferredPresence.Attach(real);
            });
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
            // T-namemc (v0.32.1): community skin gallery. NameMC has no official API; the
            // browser parses public namemc.com pages with HtmlAgilityPack. Uses its own
            // HttpClient with a 20 s timeout so slow page loads don't stall other Mojang ops.
            //
            // v0.32.2: NameMC's Cloudflare layer blocks any request whose fingerprint
            // doesn't look like a real browser - so the handler advertises automatic
            // decompression (gzip/deflate/brotli) and NameMcSkinBrowser stamps a full
            // Chrome 124 header set onto every outbound request. Without these two
            // bits the gallery returns 403 Forbidden for every call.
            var skinBrowserHandler = new System.Net.Http.SocketsHttpHandler
            {
                AutomaticDecompression =
                    System.Net.DecompressionMethods.GZip |
                    System.Net.DecompressionMethods.Deflate |
                    System.Net.DecompressionMethods.Brotli,
            };
            var skinBrowserHttp = new HttpClient(skinBrowserHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            var skinBrowser = new NameMcSkinBrowser(skinBrowserHttp);

            // Mod repositories: Modrinth always-on (no key needed); CurseForge inert until
            // the user pastes a key into Settings. Both share their own HttpClient with a
            // 20 s timeout so big project pages don't hang the UI.
            //
            // v0.32.1 (T-cf-onboarding): the CurseForge repo now reads the key through a
            // delegate so the onboarding dialog can hand a fresh value to the VM and the
            // very next SearchAsync picks it up - no launcher restart required. The VM
            // mutates this slot via SetCurseForgeApiKey on Save; the closure here just
            // surfaces the current value to the repo.
            var modsHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var modrinthRepo = new ModrinthRepository(modsHttp);
            var curseForgeHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var liveCurseForgeKey = new LiveCurseForgeKey(initialSettings.CurseForgeApiKey);
            var curseForgeRepo = new CurseForgeRepository(curseForgeHttp, liveCurseForgeKey.Read, logger);
            var instanceModManager = new FileSystemInstanceModManager();

            // Modpack import (v0.30.0 T21d). Both importers share the same instances dir under
            // LOCALAPPDATA; the dispatcher peeks at the archive contents and picks the right one.
            // Uses its own HttpClient with a generous 5-min timeout because mods are big and CDNs
            // are slow over weak connections.
            var modpackHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromMinutes(5) };
            var instancesContentRoot = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "HyperionMinecraftLauncher", "instances");
            var modrinthModpack = new ModrinthModpackImporter(instancesContentRoot, modpackHttp);
            var curseForgeModpack = new CurseForgeModpackImporter(instancesContentRoot, curseForgeRepo);
            IModpackImporter modpackImporter = new DispatchingModpackImporter(modrinthModpack, curseForgeModpack);

            // Update checker: shares the launcher's HttpClient + disk cache so the GitHub
            // releases probe is at most a once-per-hour round-trip on startup, and never
            // throws on failure (banner just stays hidden).
            var updateChecker = new GitHubReleasesUpdateChecker(httpClient, cache);
            // Headless dedicated-server registry (v0.28 T11). Folder-per-server under LOCALAPPDATA.
            var headlessServerStore = new FileHeadlessServerStore();

            // v0.32.1: real Start/Stop pipeline. The orchestrator chains the jar fetcher
            // (Mojang manifest -> server.jar download + sha1 verify), the Java auto-installer
            // (Adoptium JRE matching the MC version, resolved lazily so cold start stays light),
            // and a per-server ProcessHeadlessServer factory. The jar fetcher gets its own
            // 5-minute HttpClient because vanilla server.jar is ~45 MB and CDNs can be slow.
            var headlessJarHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var headlessJarFetcher = new MojangServerJarFetcher(headlessJarHttp);
            IHeadlessServerOrchestrator headlessServerOrchestrator = new HeadlessServerOrchestrator(
                headlessJarFetcher,
                lazyJavaRuntimeManager.Value,
                () => new ProcessHeadlessServer());

            // Per-instance world backup service (v0.30 T21f). Streams each saves/ subdir into
            // <gameDir>/backups/{worldName}-{ts}.zip before each launch, gated by
            // LauncherSettings.AutoBackupBeforeLaunch.
            var backupService = new FileSystemBackupService();
            // Instance export/import (v0.30 T21e). Default implementations; tests substitute their own.
            var instanceExporter = new FileInstanceExporter();
            var instanceImporter = new FileInstanceImporter();
            // v0.32.3 (G4): take MultiMC / Prism instance zips and folders. Lives under
            // the same instances-data root so imported instances look identical on disk to
            // FileInstanceImporter's outputs (one folder per id, no entanglement with the
            // official launcher's .minecraft/versions/).
            var multiMcImporter = new MultiMcInstanceImporter();

            // T22a (v0.31.0): localization. Builds a ResxLocalizationService over the App
            // assembly's Strings.resx family. The initial culture comes from the user's
            // persisted setting (LauncherSettings.Locale); if null we fall back to the OS
            // display language. AvailableCultures lists every culture the launcher ships
            // translations for - v0.31.0 ships 8 locales (en source + 7 translations).
            // Switching CurrentUICulture here means any code that reads
            // ResourceManager.GetString without an explicit culture (the Strings.Designer
            // properties used by AXAML's {x:Static ...}) picks up the override too.
            var initialLocale = initialSettings.Locale ?? CultureInfo.CurrentUICulture.Name;
            try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(initialLocale); }
            catch (CultureNotFoundException) { /* fall back to whatever was set */ }
            var localizationService = new ResxLocalizationService(
                TechTeaStudio.HyperionMinecraftLauncher.App.Localization.Strings.ResourceManager,
                initialLocale,
                new[] { "en", "ru", "uk", "pl", "es", "pt-BR", "de", "fr", "it", "nl", "tr", "zh-Hans", "ja", "ko" });

            // v0.32.2 (T-flyout-avatar): the account-switcher flyout now shows each row's
            // real head face. Reuse the same Mojang fetcher + on-disk cache the header chip
            // already uses, plus a per-uuid cropped-head PNG store so re-opening the launcher
            // is instant. Both ride the cache root from the shared FileCache.
            var accountSkinFetcherHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var accountSkinFetcher = new MojangPlayerSkinFetcher(accountSkinFetcherHttp, cache);
            var accountHeadCache = cache;

            var viewModel = new MainViewModel(
                service, logger, microsoftAuth, settingsStore,
                presence, instanceBrowser, skinService, skinHistory,
                modrinthRepo, curseForgeRepo, instanceModManager, accountStore,
                updateChecker, headlessServerStore, backupService, crashReportListener,
                instanceExporter, instanceImporter, modpackImporter,
                multiMcImporter,
                // v0.32.1: modLoaderInstaller is unused by the VM (it lives on the service via
                // Lazy<>). Pass null so we don't accidentally trigger the loader pipeline here.
                modLoaderInstaller: null,
                modLoaderVersionFetcher, localizationService,
                curseForgeKeySetter: liveCurseForgeKey.Set,
                skinBrowser: skinBrowser,
                headlessServerOrchestrator: headlessServerOrchestrator,
                skinFetcher: accountSkinFetcher,
                accountHeadCache: accountHeadCache);

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
    /// Thread-safe holder for the live CurseForge API key. Constructed with the value
    /// from <see cref="LauncherSettings.CurseForgeApiKey"/> at startup; <see cref="Set"/>
    /// is called by the view-model after the onboarding dialog so the next
    /// <see cref="CurseForgeRepository.SearchAsync"/> resolves the fresh key via
    /// <see cref="Read"/> without rebuilding the repository.
    /// </summary>
    private sealed class LiveCurseForgeKey
    {
        private string _value;

        public LiveCurseForgeKey(string initial)
        {
            _value = initial ?? string.Empty;
        }

        /// <summary>Current key. Volatile read is enough; we never observe torn strings.</summary>
        public string Read() => System.Threading.Volatile.Read(ref _value) ?? string.Empty;

        /// <summary>Replace the key. <c>null</c> is normalised to empty.</summary>
        public void Set(string? newKey) => System.Threading.Volatile.Write(ref _value, newKey ?? string.Empty);
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
