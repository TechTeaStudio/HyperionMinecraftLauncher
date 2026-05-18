using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.App.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;
using System.Net.Http;
using System.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

namespace TechTeaStudio.HyperionMinecraftLauncher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Manual constructor DI - no container. Mirrors the HhStoryGenerator pilot's wiring.
            var logger = new FileLauncherLogger(DefaultLogDirectory.Resolve());
            logger.Info("HyperionMinecraftLauncher starting.");

            var microsoftAuth = new MicrosoftAuthService(logger);
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

            var service = new CmlLibMinecraftLauncherService(
                new CmlLibUnderlyingLauncher(), logger, microsoftAuth,
                newsClient: newsClient,
                serverPinger: serverPinger);

            // Discord Rich Presence - obeys the LauncherSettings toggle. Read settings synchronously
            // here for the same reason MainViewModel does: the file is tiny and the wiring has to know
            // the flag before constructing the VM. Disabled / load-failed both fall back to no-op so
            // launcher startup never depends on Discord being installed.
            IPresenceService presence;
            try
            {
                var initialSettings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                presence = initialSettings.DiscordRpcEnabled
                    ? new DiscordPresenceService(logger)
                    : new NullPresenceService();
            }
            catch (System.Exception ex)
            {
                logger.Warn($"Could not init Discord presence ({ex.Message}); falling back to no-op.");
                presence = new NullPresenceService();
            }

            var viewModel = new MainViewModel(service, logger, microsoftAuth, settingsStore, presence);

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
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _ = viewModel.RunStartupRefreshesAsync(),
                Avalonia.Threading.DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
