using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;
using System.Net.Http;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
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

            var service = new CmlLibMinecraftLauncherService(
                new CmlLibUnderlyingLauncher(), logger, microsoftAuth,
                newsClient: newsClient);

            var viewModel = new MainViewModel(service, logger, microsoftAuth, settingsStore, accountStore);

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
