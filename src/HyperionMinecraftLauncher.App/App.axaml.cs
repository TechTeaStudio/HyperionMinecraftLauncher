using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;
using System.Net.Http;
using System.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
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

            var service = new CmlLibMinecraftLauncherService(
                new CmlLibUnderlyingLauncher(), logger, microsoftAuth,
                newsClient: newsClient);

            // Mod repositories: Modrinth always-on (no key needed); CurseForge inert until
            // the user pastes a key into Settings. Both share their own HttpClient with a
            // 20 s timeout so big project pages don't hang the UI.
            var modsHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var modrinthRepo = new ModrinthRepository(modsHttp);
            var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var curseForgeHttp = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
            var curseForgeRepo = new CurseForgeRepository(curseForgeHttp, settings.CurseForgeApiKey, logger);
            var instanceModManager = new FileSystemInstanceModManager();

            var viewModel = new MainViewModel(
                service, logger, microsoftAuth, settingsStore,
                modrinthRepo, curseForgeRepo, instanceModManager);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

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
