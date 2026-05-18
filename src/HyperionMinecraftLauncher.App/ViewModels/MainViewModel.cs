using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using MinecraftSkinRender.Image;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;
using TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Util;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// View-model behind <c>MainWindow.axaml</c>. Owns the sidebar selection, the account
/// state (offline vs Microsoft-signed-in), the manifest + installed version lists, the
/// log, and every command the window exposes.
/// </summary>
/// <remarks>
/// One view-model for the whole window is fine while every "page" is either the Home
/// page (real UI) or a "Coming soon" placeholder. When the placeholders graduate to
/// real pages in later releases, each will own its own view-model and this one becomes
/// a thin shell.
/// </remarks>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IMinecraftLauncherService _service;
    private readonly ILauncherLogger _logger;
    private readonly IMicrosoftAuthService? _microsoftAuth;
    private readonly ILauncherSettingsStore? _settingsStore;
    private readonly IPresenceService _presence;
    private readonly IInstanceBrowser? _instanceBrowser;
    private readonly ICrashReportListener? _crashReportListener;
    private readonly ISkinService? _skinService;
    private readonly ISkinHistoryStore? _skinHistory;
    private SkinPickRequest? _skinPickRequest;

    // T-namemc (v0.32.1): community skin gallery. Optional - the Skins page falls
    // back to "Browser not available" copy when the user runs the launcher without
    // outbound HTTP or in a test context.
    private readonly ISkinBrowser? _skinBrowser;
    private string _skinSearchText = string.Empty;
    private BrowsedSkin? _selectedBrowsedSkin;
    private bool _skinBrowserBusy;
    private readonly IModRepository? _modrinthRepository;
    private readonly IModRepository? _curseForgeRepository;
    private readonly IInstanceModManager? _instanceModManager;
    private readonly IAccountStore? _accountStore;
    // v0.32.2 (T-flyout-avatar): optional Mojang skin fetcher used to populate each
    // account-switcher row's mini head-face. Without it the rows fall back to the bundled
    // Steve face (still functional, just visually wrong for the active row's real skin).
    private readonly IPlayerSkinFetcher? _skinFetcher;
    private readonly FileCache? _accountHeadCache;
    private readonly IUpdateChecker? _updateChecker;
    private UpdateInfo? _availableUpdate;
    private bool _autoUpdateCheckEnabled;
    private readonly IHeadlessServerStore? _headlessServerStore;
    private readonly IHeadlessServerOrchestrator? _headlessServerOrchestrator;
    private HeadlessServer? _selectedHeadlessServer;
    // v0.32.1: real start/stop. Tracks per-server state + the cap-bounded console pane buffer
    // for whichever server is currently selected on the Headless Servers page. We keep the
    // line list per-server so flipping the selection doesn't lose the live stdout for the
    // one running in the background.
    private readonly Dictionary<string, ObservableCollection<string>> _headlessConsoleLines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDisposable> _headlessConsoleSubscriptions = new(StringComparer.Ordinal);
    private string _headlessCommandInput = string.Empty;
    /// <summary>Soft cap on accumulated stdout lines per server (prevents unbounded memory growth on long runs).</summary>
    private const int HeadlessConsoleLineCap = 10000;
    private readonly IBackupService? _backupService;
    private bool _autoBackupBeforeLaunch;
    private int _autoBackupKeepLatest;
    private readonly IInstanceExporter? _instanceExporter;
    private readonly IInstanceImporter? _instanceImporter;
    private InstanceExportZipPickRequest? _exportZipPickRequest;
    private InstanceImportZipPickRequest? _importZipPickRequest;
    private readonly IModpackImporter? _modpackImporter;
    private double _modpackImportProgress;
    private bool _isImportingModpack;
    private string _modpackImportStatus = string.Empty;

    // T22a (v0.31.0): localization. Optional - tests + CLI contexts can leave it null. The
    // settings page locale dropdown writes the user's choice through SetCultureAsync; the
    // service fires LanguageChanged so XAML bindings of the form
    // {Binding [Key], Source={x:Static loc:LocalizationService.Default}} pick up the new
    // value live. Plain {x:Static loc:Strings.X} bindings keep their startup value and need
    // a restart to pick up the new culture - documented in Localization/README.md.
    private readonly ILocalizationService? _localizationService;
    private string? _locale;

    // T21a, v0.30.0: mod-loader install pipeline. The installer downloads + writes the
    // matching version folder under .minecraft/versions/; the version fetcher queries the
    // available loader versions for the New Instance dialog. Both are optional - tests and
    // CLI contexts can leave them null and lose only the loader features.
    private readonly IModLoaderInstaller? _modLoaderInstaller;
    private readonly IModLoaderVersionFetcher? _modLoaderVersionFetcher;

    // Settings-page state (mirrored from LauncherSettings on load, written back on Save).
    private int _minMemoryMb;
    private int _maxMemoryMb;
    private string _jvmArguments = string.Empty;
    private string _gameDirectoryOverride = string.Empty;
    private string _javaExecutableOverride = string.Empty;
    private bool _keepLauncherOpen;
    private bool _showGameLog;
    private bool _sidebarCollapsed;
    private string _curseForgeApiKey = string.Empty;
    private readonly int _maxAllowedMemoryMb;

    // v0.32.1 (T-cf-onboarding): the App passes this delegate down so the VM can push a
    // freshly-entered CurseForge API key into the live CurseForgeRepository slot. Without
    // it the user would have to restart the launcher between pasting the key and the next
    // search hitting the network.
    private readonly Action<string?>? _curseForgeKeySetter;

    // Injected by the View on Window.Opened (same pattern as SkinPickRequest). Returns the
    // entered key on Save, or null when the user cancelled the dialog.
    private CurseForgeKeyRequest? _curseForgeKeyRequest;

    // Sidebar widths in DIPs - kept in one place so XAML transitions hit predictable targets.
    /// <summary>Sidebar width when expanded (icons + labels). Matches the legacy fixed-column width.</summary>
    public const double SidebarExpandedWidth = 180.0;
    /// <summary>Sidebar width when collapsed (icons only, labels hidden, tooltips on hover).</summary>
    public const double SidebarCollapsedWidth = 56.0;

    private string _username = "Steve";
    private string _versionSearchText = string.Empty;
    private bool _showRelease = true;
    private bool _showSnapshot;
    private bool _showOldBeta;
    private bool _showOldAlpha;
    private VersionMetadata? _selectedVersion;
    private InstalledVersion? _selectedInstalledVersion;
    private LauncherProfile? _selectedProfile;
    private Instance? _selectedInstance;
    private string _logText = string.Empty;
    private bool _isBusy;
    private AuthResult? _currentSession;
    private NavSection _selectedSection = NavSection.Home;
    private Bitmap? _avatarBitmap;
    private IReadOnlyList<ScreenshotEntry> _instanceScreenshots = Array.Empty<ScreenshotEntry>();
    private IReadOnlyList<WorldEntry> _instanceWorlds = Array.Empty<WorldEntry>();
    private IReadOnlyList<ServerListEntry> _instanceServers = Array.Empty<ServerListEntry>();
    private IReadOnlyList<CrashReport> _instanceCrashReports = Array.Empty<CrashReport>();
    // v0.32.1 T-content-tabs: per-instance content packs. The collections back the three new
    // tabs (Resource packs / Shader packs / Data packs) on the Installations detail panel.
    // Toggle/Remove commands rename / delete the file in place and then trigger a refresh.
    private CrashReport? _selectedCrashReport;
    private InstanceDetailTab _selectedInstanceTab = InstanceDetailTab.Screenshots;
    private IReadOnlyList<OwnedSkin> _ownedSkins = Array.Empty<OwnedSkin>();
    private IReadOnlyList<OwnedCape> _ownedCapes = Array.Empty<OwnedCape>();
    private OwnedCape? _selectedActiveCape;
    private bool _suppressCapeSelectionWrite;

    // Logs page state: the full contents of today's launcher log file, the user-typed filter,
    // and the timestamp of the last successful refresh. FilteredLogText is recomputed on demand
    // from LogFileText + LogFilter through the LogFiltering helper.
    private string _logFileText = string.Empty;
    private string _logFilter = string.Empty;
    private DateTime _logLastRefreshed;

    /// <summary>Construct with the launcher service and optional auth/settings/presence/browser/skin services.</summary>
    // Mods page state.
    private string _modSearchTerm = string.Empty;
    private ModSource _selectedModSource = ModSource.Modrinth;
    private Mod? _selectedModSearchResult;
    private LocalMod? _selectedInstalledMod;

    /// <summary>Construct with the launcher service and (optional) Microsoft auth provider + settings store + mod repos / manager.</summary>
    private Account? _activeAccount;

    /// <summary>Construct with the launcher service and (optional) Microsoft auth provider + settings store + account store.</summary>
    public MainViewModel(
        IMinecraftLauncherService service,
        ILauncherLogger logger,
        IMicrosoftAuthService? microsoftAuth = null,
        ILauncherSettingsStore? settingsStore = null,
        IPresenceService? presence = null,
        IInstanceBrowser? instanceBrowser = null,
        ISkinService? skinService = null,
        ISkinHistoryStore? skinHistory = null,
        IModRepository? modrinthRepository = null,
        IModRepository? curseForgeRepository = null,
        IInstanceModManager? instanceModManager = null,
        IAccountStore? accountStore = null,
        IUpdateChecker? updateChecker = null,
        IHeadlessServerStore? headlessServerStore = null,
        IBackupService? backupService = null,
        ICrashReportListener? crashReportListener = null,
        IInstanceExporter? instanceExporter = null,
        IInstanceImporter? instanceImporter = null,
        IModpackImporter? modpackImporter = null,
        IModLoaderInstaller? modLoaderInstaller = null,
        IModLoaderVersionFetcher? modLoaderVersionFetcher = null,
        ILocalizationService? localizationService = null,
        Action<string?>? curseForgeKeySetter = null,
        ISkinBrowser? skinBrowser = null,
        IHeadlessServerOrchestrator? headlessServerOrchestrator = null,
        IPlayerSkinFetcher? skinFetcher = null,
        FileCache? accountHeadCache = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _microsoftAuth = microsoftAuth;
        _settingsStore = settingsStore;
        // Default to no-op so the constructor stays test-friendly when callers don't care.
        _presence = presence ?? new NullPresenceService();
        _instanceBrowser = instanceBrowser;
        _crashReportListener = crashReportListener;
        _skinService = skinService;
        _skinHistory = skinHistory;
        _skinBrowser = skinBrowser;
        SkinHistory = new ObservableCollection<SkinHistoryEntry>();
        BrowsedSkins = new ObservableCollection<BrowsedSkin>();
        _modrinthRepository = modrinthRepository;
        _curseForgeRepository = curseForgeRepository;
        _instanceModManager = instanceModManager;
        _accountStore = accountStore;
        _updateChecker = updateChecker;
        _headlessServerStore = headlessServerStore;
        _backupService = backupService;
        _instanceExporter = instanceExporter;
        _instanceImporter = instanceImporter;
        _modLoaderInstaller = modLoaderInstaller;
        _modLoaderVersionFetcher = modLoaderVersionFetcher;
        _localizationService = localizationService;
        _curseForgeKeySetter = curseForgeKeySetter;
        _skinFetcher = skinFetcher;
        _accountHeadCache = accountHeadCache;
        _headlessServerOrchestrator = headlessServerOrchestrator;
        if (_headlessServerOrchestrator is not null)
            _headlessServerOrchestrator.StateChanged += OnHeadlessOrchestratorStateChanged;
        HeadlessServers = new ObservableCollection<HeadlessServer>();
        _modpackImporter = modpackImporter;
        _maxAllowedMemoryMb = SystemRam.RecommendedMaxHeapMb();

        // MSAL device-code prompts come from a background thread; surface them in the UI log.
        if (_microsoftAuth is not null)
            _microsoftAuth.DeviceCodeRequested += OnDeviceCodeRequested;

        // v0.32.1 (T-startup-perf): settings used to be read synchronously in the ctor (~30 ms
        // file read + JSON parse on a cold disk). The first paint of the main window has no
        // dependency on these values - the memory/jvm/locale defaults are sane enough for the
        // bound controls until ApplyLoadedSettingsAsync re-applies the real values during
        // RunStartupRefreshesAsync. Tests that need specific values can still call
        // ApplyLoadedSettingsAsync directly (or use the synchronous ApplySettings overload on
        // an injected LauncherSettings).
        ApplySettings(new LauncherSettings());

        AvailableVersions = new ObservableCollection<VersionMetadata>();
        FilteredVersions = new ObservableCollection<VersionMetadata>();
        InstalledVersions = new ObservableCollection<InstalledVersion>();
        Profiles = new ObservableCollection<LauncherProfile>();
        Servers = new ObservableCollection<ServerListItemViewModel>();
        News = new ObservableCollection<NewsEntry>();
        Instances = new ObservableCollection<Instance>();
        ModSearchResults = new ObservableCollection<Mod>();
        InstalledMods = new ObservableCollection<LocalMod>();
        Accounts = new ObservableCollection<AccountWithBitmap>();
        InstanceResourcePacks = new ObservableCollection<ResourcePackEntry>();
        InstanceShaderPacks = new ObservableCollection<ShaderPackEntry>();
        InstanceDataPacks = new ObservableCollection<DataPackEntry>();

        RefreshVersionsCommand = new AsyncRelayCommand(RefreshVersionsAsync, () => !IsBusy);
        RefreshInstalledVersionsCommand = new AsyncRelayCommand(RefreshInstalledVersionsAsync, () => !IsBusy);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync, () => !IsBusy);
        RefreshServersCommand = new AsyncRelayCommand(RefreshServersAsync, () => !IsBusy);
        RefreshServerPingsCommand = new AsyncRelayCommand(RefreshServerPingsAsync, () => !IsBusy && Servers.Count > 0);
        RefreshNewsCommand = new AsyncRelayCommand(RefreshNewsAsync, () => !IsBusy);
        RefreshInstancesCommand = new AsyncRelayCommand(RefreshInstancesAsync, () => !IsBusy);
        DeleteInstanceCommand = new AsyncRelayCommand(
            DeleteSelectedInstanceAsync,
            () => !IsBusy && SelectedInstance is { IsAutoImported: false });
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy && _settingsStore is not null);
        ToggleSidebarCommand = new AsyncRelayCommand(ToggleSidebarAsync, () => true);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanLaunch);
        SignInMicrosoftCommand = new AsyncRelayCommand(SignInMicrosoftAsync, () => !IsBusy && !IsSignedInOnline);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, () => !IsBusy && IsSignedInOnline);
        RefreshLogFileCommand = new AsyncRelayCommand(LoadTodaysLogAsync, () => !IsBusy);
        RefreshInstanceScreenshotsCommand = new AsyncRelayCommand(
            RefreshInstanceScreenshotsAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        RefreshInstanceWorldsCommand = new AsyncRelayCommand(
            RefreshInstanceWorldsAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        RefreshInstanceServersCommand = new AsyncRelayCommand(
            RefreshInstanceServersAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        RefreshInstanceCrashReportsCommand = new AsyncRelayCommand(
            RefreshInstanceCrashReportsAsync,
            () => !IsBusy && SelectedInstance is not null && _crashReportListener is not null);

        // v0.32.1 T-content-tabs: refresh + per-row Toggle / Remove commands for the three
        // new pack tabs. Toggle renames .zip <-> .zip.disabled in place; Remove deletes the
        // file. Both commands then re-list to update the UI.
        RefreshInstanceResourcePacksCommand = new AsyncRelayCommand(
            RefreshInstanceResourcePacksAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        RefreshInstanceShaderPacksCommand = new AsyncRelayCommand(
            RefreshInstanceShaderPacksAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        RefreshInstanceDataPacksCommand = new AsyncRelayCommand(
            RefreshInstanceDataPacksAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceBrowser is not null);
        ToggleResourcePackCommand = new AsyncParameterRelayCommand<string>(
            ToggleResourcePackAsync,
            n => !IsBusy && SelectedInstance is not null && !string.IsNullOrEmpty(n));
        ToggleShaderPackCommand = new AsyncParameterRelayCommand<string>(
            ToggleShaderPackAsync,
            n => !IsBusy && SelectedInstance is not null && !string.IsNullOrEmpty(n));
        ToggleDataPackCommand = new AsyncParameterRelayCommand<DataPackEntry>(
            ToggleDataPackAsync,
            e => !IsBusy && SelectedInstance is not null && e is not null);
        RemoveResourcePackCommand = new AsyncParameterRelayCommand<string>(
            RemoveResourcePackAsync,
            n => !IsBusy && SelectedInstance is not null && !string.IsNullOrEmpty(n));
        RemoveShaderPackCommand = new AsyncParameterRelayCommand<string>(
            RemoveShaderPackAsync,
            n => !IsBusy && SelectedInstance is not null && !string.IsNullOrEmpty(n));
        RemoveDataPackCommand = new AsyncParameterRelayCommand<DataPackEntry>(
            RemoveDataPackAsync,
            e => !IsBusy && SelectedInstance is not null && e is not null);
        OpenCrashReportInBrowserCommand = new AsyncRelayCommand(
            OpenSelectedCrashReportInBrowserAsync,
            () => SelectedCrashReport is not null && SelectedCrashReport.SuspectedMods.Count > 0);
        UploadSkinCommand = new AsyncRelayCommand(UploadSkinAsync, () => !IsBusy && IsSignedInOnline && _skinService is not null);
        SetActiveCapeCommand = new AsyncRelayCommand(SetActiveCapeFromSelectionAsync, () => !IsBusy && IsSignedInOnline && _skinService is not null);
        ClearActiveCapeCommand = new AsyncRelayCommand(ClearActiveCapeAsync, () => !IsBusy && IsSignedInOnline && _skinService is not null);
        ReapplyHistoricSkinCommand = new AsyncRelayCommand<SkinHistoryEntry>(ReapplyHistoricSkinAsync, e => !IsBusy && IsSignedInOnline && _skinService is not null && e is not null);

        // T-namemc (v0.32.1): community skin browser commands.
        // Refresh/Search fetch a gallery from the source (NameMC by default); Apply
        // downloads the chosen skin PNG and pipes it through ISkinService.UploadSkinAsync.
        RefreshTrendingSkinsCommand = new AsyncRelayCommand(
            RefreshTrendingBrowsedSkinsAsync,
            () => !_skinBrowserBusy && _skinBrowser is not null);
        SearchSkinsCommand = new AsyncRelayCommand(
            SearchBrowsedSkinsAsync,
            () => !_skinBrowserBusy && _skinBrowser is not null);
        ApplyBrowsedSkinCommand = new AsyncRelayCommand<BrowsedSkin>(
            ApplyBrowsedSkinAsync,
            s => !IsBusy && IsSignedInOnline && _skinService is not null && _skinBrowser is not null && s is not null);

        SearchModsCommand = new AsyncRelayCommand(SearchModsAsync, () => !IsBusy && GetActiveModRepository() is not null);
        InstallModCommand = new AsyncRelayCommand(InstallSelectedModAsync,
            () => !IsBusy && SelectedModSearchResult is not null && SelectedInstance is not null && _instanceModManager is not null);
        RefreshInstalledModsCommand = new AsyncRelayCommand(RefreshInstalledModsAsync,
            () => !IsBusy && SelectedInstance is not null && _instanceModManager is not null);
        ToggleInstalledModCommand = new AsyncRelayCommand(ToggleSelectedInstalledModAsync,
            () => !IsBusy && SelectedInstalledMod is not null && SelectedInstance is not null && _instanceModManager is not null);
        RemoveInstalledModCommand = new AsyncRelayCommand(RemoveSelectedInstalledModAsync,
            () => !IsBusy && SelectedInstalledMod is not null && SelectedInstance is not null && _instanceModManager is not null);

        // T-cf-onboarding (v0.32.1): the "Set up CurseForge" entry points - Settings card,
        // Mods empty-state panel - all route through this command. The View injects a
        // CurseForgeKeyRequest delegate on Window.Opened; the VM calls it, persists the
        // result through the settings store, and pushes the live key into the repository
        // via _curseForgeKeySetter so the next search hits the network without restart.
        OpenCurseForgeOnboardingCommand = new AsyncRelayCommand(
            OpenCurseForgeOnboardingAsync,
            () => !IsBusy && _curseForgeKeyRequest is not null && _settingsStore is not null);

        // Multi-account roster (v0.27.0): switch silently by id, add a fresh device-code
        // sign-in, or sign-out + remove the chosen account from the cache.
        // v0.32.2 (T-flyout-avatar): SwitchAccount now takes the AccountWithBitmap projection
        // (flyout rows bind to those), but unwraps to the underlying Account internally.
        // RemoveAccount still takes Account because the X button's CommandParameter is bound
        // to ActiveAccount which stays an Account.
        SwitchAccountCommand = new AsyncParameterRelayCommand<AccountWithBitmap>(
            SwitchAccountAsync,
            a => !IsBusy && a is not null && _microsoftAuth is not null);
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => !IsBusy && _microsoftAuth is not null);
        RemoveAccountCommand = new AsyncParameterRelayCommand<Account>(
            RemoveAccountAsync,
            a => !IsBusy && a is not null && _microsoftAuth is not null);

        // Launcher update banner: only enabled once the startup probe populates AvailableUpdate;
        // Dismiss simply clears the banner for the current session (re-checked on next startup).
        OpenReleasePageCommand = new AsyncRelayCommand(OpenReleasePageAsync, () => AvailableUpdate is not null);
        DismissUpdateCommand = new AsyncRelayCommand(DismissUpdateAsync, () => AvailableUpdate is not null);

        // Bug 5 (v0.32.0): manual "Check for updates" button on the Settings page. The
        // startup probe was the only entry point before; it relied on the user having
        // auto-checks enabled AND a strictly-newer GitHub release existing, so the
        // banner never surfaced in practice. This command runs the same checker on
        // demand and either populates AvailableUpdate (banner appears) or appends a
        // confirmation log line so the user always gets a response.
        CheckForUpdatesCommand = new AsyncRelayCommand(
            CheckForUpdatesAsync,
            () => !IsBusy && _updateChecker is not null);

        // Bug 3 (v0.32.0): the per-instance detail panel had no dismiss affordance, so once
        // a tile was clicked the user was stuck looking at it. This command nulls
        // SelectedInstance, which collapses the whole Border via the HasSelectedInstance
        // IsVisible binding. Enabled only when an instance is actually selected.
        CloseInstanceDetailCommand = new AsyncRelayCommand(
            CloseInstanceDetailAsync,
            () => SelectedInstance is not null);
        // Headless servers (v0.28 T11). Refresh repopulates the page list; Delete drops the
        // selected entry; Start / Stop are placeholders until the v0.29 server-jar pipeline lands.
        RefreshHeadlessServersCommand = new AsyncRelayCommand(
            RefreshHeadlessServersAsync,
            () => !IsBusy && _headlessServerStore is not null);
        DeleteHeadlessServerCommand = new AsyncRelayCommand(
            DeleteSelectedHeadlessServerAsync,
            () => !IsBusy && SelectedHeadlessServer is not null && _headlessServerStore is not null);
        StartHeadlessServerCommand = new AsyncRelayCommand(
            StartSelectedHeadlessServerAsync,
            () => !IsBusy
                && SelectedHeadlessServer is not null
                && _headlessServerOrchestrator is not null
                && _headlessServerOrchestrator.GetState(SelectedHeadlessServer.Id) == HeadlessServerState.Stopped);
        StopHeadlessServerCommand = new AsyncRelayCommand(
            StopSelectedHeadlessServerAsync,
            () => !IsBusy
                && SelectedHeadlessServer is not null
                && _headlessServerOrchestrator is not null
                && _headlessServerOrchestrator.GetState(SelectedHeadlessServer.Id) is HeadlessServerState.Running or HeadlessServerState.Starting);
        // v0.32.1: send a typed command to the running server's stdin. Only valid while the
        // selected server is in the Running state. The text box clears on success.
        SendHeadlessCommand = new AsyncRelayCommand(
            SendHeadlessCommandAsync,
            () => !IsBusy
                && SelectedHeadlessServer is not null
                && _headlessServerOrchestrator is not null
                && _headlessServerOrchestrator.GetState(SelectedHeadlessServer.Id) == HeadlessServerState.Running
                && !string.IsNullOrWhiteSpace(_headlessCommandInput));

        // Per-world backup actions on the Worlds tab "..." menu. Parameter is the
        // WorldEntry's FolderName so the XAML can bind directly to {Binding FolderName}.
        BackupWorldCommand = new AsyncParameterRelayCommand<string>(
            BackupWorldAsync,
            n => !IsBusy && _backupService is not null && SelectedInstance is not null && !string.IsNullOrEmpty(n));
        RestoreLatestBackupCommand = new AsyncParameterRelayCommand<string>(
            RestoreLatestBackupAsync,
            n => !IsBusy && _backupService is not null && SelectedInstance is not null && !string.IsNullOrEmpty(n));

        // Instance export/import (v0.30 T21e). Export uses the currently-selected instance;
        // Import asks the View for an existing zip and bolts it into the store on success.
        ExportInstanceCommand = new AsyncRelayCommand(
            ExportSelectedInstanceAsync,
            () => !IsBusy && SelectedInstance is { IsAutoImported: false } && _instanceExporter is not null);
        ImportInstanceCommand = new AsyncRelayCommand(
            ImportInstanceAsync,
            () => !IsBusy && _instanceImporter is not null);
    }

    /// <summary>"Backup now" entry on the Worlds tab. Parameter = world folder name.</summary>
    public AsyncParameterRelayCommand<string> BackupWorldCommand { get; private set; } = null!;

    /// <summary>"Restore latest backup" entry on the Worlds tab. Parameter = world folder name.</summary>
    public AsyncParameterRelayCommand<string> RestoreLatestBackupCommand { get; private set; } = null!;

    private async Task BackupWorldAsync(string? worldFolderName)
    {
        if (_backupService is null || SelectedInstance is not { } inst || string.IsNullOrEmpty(worldFolderName)) return;
        try
        {
            Append($"[backup] Snapshotting '{worldFolderName}' ...");
            var entry = await _backupService.BackupWorldAsync(inst, worldFolderName, CancellationToken.None).ConfigureAwait(true);
            if (entry is null)
            {
                Append($"[warn] '{worldFolderName}' has no files to back up.");
                return;
            }
            var mb = entry.SizeBytes / 1024.0 / 1024.0;
            var label = mb >= 1
                ? $"{mb.ToString("0.#", CultureInfo.InvariantCulture)} MB"
                : $"{(entry.SizeBytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";
            Append($"[backup] {Path.GetFileName(entry.ArchivePath)} ({label})");
            if (_autoBackupKeepLatest > 0)
                await _backupService.PruneAsync(inst, _autoBackupKeepLatest, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append($"[error] Backup '{worldFolderName}' failed: {ex.Message}");
        }
    }

    private async Task RestoreLatestBackupAsync(string? worldFolderName)
    {
        if (_backupService is null || SelectedInstance is not { } inst || string.IsNullOrEmpty(worldFolderName)) return;
        try
        {
            var all = await _backupService.ListBackupsAsync(inst, CancellationToken.None).ConfigureAwait(true);
            BackupEntry? latest = null;
            foreach (var e in all)
            {
                if (string.Equals(e.SourceWorldName, worldFolderName, StringComparison.OrdinalIgnoreCase)
                    && (latest is null || e.CreatedAt > latest.CreatedAt))
                    latest = e;
            }
            if (latest is null)
            {
                Append($"[warn] No backups found for '{worldFolderName}'.");
                return;
            }
            Append($"[backup] Restoring '{worldFolderName}' from {Path.GetFileName(latest.ArchivePath)} ...");
            await _backupService.RestoreAsync(latest, CancellationToken.None).ConfigureAwait(true);
            Append($"[backup] Restored as '{worldFolderName}-restored-...'. The original world folder is untouched.");
            // Refresh the worlds list so the new -restored-* folder shows up immediately.
            if (RefreshInstanceWorldsCommand.CanExecute(null))
                await RefreshInstanceWorldsCommand.ExecuteAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append($"[error] Restore '{worldFolderName}' failed: {ex.Message}");
        }
    }

    // ---- Account ----

    /// <summary>True when the user is signed in via Microsoft (not offline).</summary>
    public bool IsSignedInOnline => _currentSession is { IsOffline: false };

    /// <summary>True when there is any session (offline or Microsoft). Drives the account-chip label visibility.</summary>
    public bool HasSession => _currentSession is not null;

    /// <summary>
    /// Avatar bitmap shown in the header account chip and the sidebar. The View pushes this
    /// after a skin is loaded - either the bundled Steve face on startup, or the head face
    /// cropped from the user's real skin after Microsoft sign-in.
    /// </summary>
    public Bitmap? AvatarBitmap
    {
        get => _avatarBitmap;
        set => SetField(ref _avatarBitmap, value);
    }

    /// <summary>Display label for the account chip when a session exists. Empty when not signed in (the Sign-in button speaks for itself).</summary>
    public string AccountDisplay => _currentSession is null
        ? string.Empty
        : _currentSession.IsOffline
            ? $"Offline: {_currentSession.Username}"
            : _currentSession.Username;

    /// <summary>The current authenticated session, if any.</summary>
    public AuthResult? CurrentSession
    {
        get => _currentSession;
        private set
        {
            if (SetField(ref _currentSession, value))
            {
                OnPropertyChanged(nameof(IsSignedInOnline));
                OnPropertyChanged(nameof(HasSession));
                OnPropertyChanged(nameof(AccountDisplay));
                SignInMicrosoftCommand.RaiseCanExecuteChanged();
                SignOutCommand.RaiseCanExecuteChanged();
                LaunchCommand.RaiseCanExecuteChanged();
                UploadSkinCommand.RaiseCanExecuteChanged();
                SetActiveCapeCommand.RaiseCanExecuteChanged();
                ClearActiveCapeCommand.RaiseCanExecuteChanged();
                ReapplyHistoricSkinCommand.RaiseCanExecuteChanged();
                ApplyBrowsedSkinCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Every cached account known to the launcher (Microsoft + offline placeholders), each
    /// wrapped with a pre-loaded head-face bitmap so the account-switcher flyout shows the
    /// real skin instead of the bundled Steve. The wrapper exposes <c>Id</c>, <c>Username</c>,
    /// <c>Uuid</c>, and <c>IsOffline</c> pass-throughs so the existing XAML bindings keep
    /// working unchanged.
    /// </summary>
    public ObservableCollection<AccountWithBitmap> Accounts { get; }

    /// <summary>The account whose session is currently in <see cref="CurrentSession"/>. <c>null</c> on a fresh install.</summary>
    public Account? ActiveAccount
    {
        get => _activeAccount;
        private set
        {
            if (SetField(ref _activeAccount, value))
            {
                RemoveAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ---- Versions ----

    /// <summary>The offline username the user typed; only used when no Microsoft session is active.</summary>
    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    /// <summary>Versions returned by the Mojang manifest (full list of installable versions).</summary>
    public ObservableCollection<VersionMetadata> AvailableVersions { get; }

    /// <summary>
    /// View over <see cref="AvailableVersions"/> filtered by <see cref="VersionSearchText"/>
    /// and the four type chips (Release / Snapshot / Old beta / Old alpha). Rebuilt by
    /// <see cref="ApplyVersionFilter"/> whenever the search text or a chip changes, and
    /// after <see cref="RefreshVersionsAsync"/> repopulates the source list.
    /// </summary>
    public ObservableCollection<VersionMetadata> FilteredVersions { get; }

    /// <summary>Free-text search over both <c>Name</c> (the manifest id) and <c>DisplayText</c>. Case-insensitive.</summary>
    public string VersionSearchText
    {
        get => _versionSearchText;
        set
        {
            if (SetField(ref _versionSearchText, value ?? string.Empty))
                ApplyVersionFilter();
        }
    }

    /// <summary>Filter chip: include manifest entries with type <c>"release"</c>. Default on.</summary>
    public bool ShowRelease
    {
        get => _showRelease;
        set
        {
            if (SetField(ref _showRelease, value))
                ApplyVersionFilter();
        }
    }

    /// <summary>Filter chip: include manifest entries with type <c>"snapshot"</c>. Default off.</summary>
    public bool ShowSnapshot
    {
        get => _showSnapshot;
        set
        {
            if (SetField(ref _showSnapshot, value))
                ApplyVersionFilter();
        }
    }

    /// <summary>Filter chip: include manifest entries with type <c>"old_beta"</c>. Default off.</summary>
    public bool ShowOldBeta
    {
        get => _showOldBeta;
        set
        {
            if (SetField(ref _showOldBeta, value))
                ApplyVersionFilter();
        }
    }

    /// <summary>Filter chip: include manifest entries with type <c>"old_alpha"</c>. Default off.</summary>
    public bool ShowOldAlpha
    {
        get => _showOldAlpha;
        set
        {
            if (SetField(ref _showOldAlpha, value))
                ApplyVersionFilter();
        }
    }

    /// <summary>
    /// Rebuild <see cref="FilteredVersions"/> from <see cref="AvailableVersions"/> using the
    /// current chip flags and search text. Two predicates AND-ed together: the entry's
    /// <c>Type</c> must match an enabled chip (case-insensitive) AND, if search text is
    /// non-empty, either <c>Name</c> or <c>DisplayText</c> must contain the text (case-insensitive).
    /// If the previously-selected version no longer satisfies the filter, fall back to the
    /// first surviving entry so the bound ComboBox doesn't render a stale selection.
    /// </summary>
    public void ApplyVersionFilter()
    {
        FilteredVersions.Clear();
        var search = _versionSearchText;
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        foreach (var v in AvailableVersions)
        {
            if (!TypeMatches(v.Type)) continue;
            if (hasSearch
                && v.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && v.DisplayText.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            FilteredVersions.Add(v);
        }

        // Keep the bound ComboBox honest: if the previous selection has been filtered out,
        // jump to the first surviving entry (or null when the filter is empty).
        if (_selectedVersion is null || !FilteredVersions.Contains(_selectedVersion))
            SelectedVersion = FilteredVersions.FirstOrDefault();
    }

    private bool TypeMatches(string type)
    {
        if (string.Equals(type, "release", StringComparison.OrdinalIgnoreCase)) return _showRelease;
        if (string.Equals(type, "snapshot", StringComparison.OrdinalIgnoreCase)) return _showSnapshot;
        if (string.Equals(type, "old_beta", StringComparison.OrdinalIgnoreCase)) return _showOldBeta;
        if (string.Equals(type, "old_alpha", StringComparison.OrdinalIgnoreCase)) return _showOldAlpha;
        // Unknown / future type strings ride along with Release so they aren't silently hidden.
        return _showRelease;
    }

    /// <summary>Versions already present on disk under <c>.minecraft/versions/</c>.</summary>
    public ObservableCollection<InstalledVersion> InstalledVersions { get; }

    /// <summary>Profiles parsed from Mojang's <c>launcher_profiles.json</c>.</summary>
    public ObservableCollection<LauncherProfile> Profiles { get; }

    /// <summary>
    /// Multiplayer servers parsed from <c>servers.dat</c>, wrapped so each row carries the latest
    /// ping status alongside the underlying NBT entry.
    /// </summary>
    public ObservableCollection<ServerListItemViewModel> Servers { get; }

    /// <summary>News from Mojang's launcher feed.</summary>
    public ObservableCollection<NewsEntry> News { get; }

    /// <summary>Hyperion launcher instances (created via the New Instance dialog).</summary>
    public ObservableCollection<Instance> Instances { get; }

    /// <summary>The instance currently selected on the Installations page tile grid.</summary>
    public Instance? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetField(ref _selectedInstance, value))
            {
                DeleteInstanceCommand.RaiseCanExecuteChanged();
                LaunchCommand.RaiseCanExecuteChanged();
                RefreshInstanceScreenshotsCommand.RaiseCanExecuteChanged();
                RefreshInstanceWorldsCommand.RaiseCanExecuteChanged();
                RefreshInstanceServersCommand.RaiseCanExecuteChanged();
                RefreshInstanceCrashReportsCommand.RaiseCanExecuteChanged();
                RefreshInstanceResourcePacksCommand?.RaiseCanExecuteChanged();
                RefreshInstanceShaderPacksCommand?.RaiseCanExecuteChanged();
                RefreshInstanceDataPacksCommand?.RaiseCanExecuteChanged();
                ToggleResourcePackCommand?.RaiseCanExecuteChanged();
                ToggleShaderPackCommand?.RaiseCanExecuteChanged();
                ToggleDataPackCommand?.RaiseCanExecuteChanged();
                RemoveResourcePackCommand?.RaiseCanExecuteChanged();
                RemoveShaderPackCommand?.RaiseCanExecuteChanged();
                RemoveDataPackCommand?.RaiseCanExecuteChanged();
                ExportInstanceCommand?.RaiseCanExecuteChanged();
                CloseInstanceDetailCommand?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelectedInstance));
                // Auto-refresh per-instance detail tabs when the selected instance changes.
                // Fire-and-forget: the View animates a fade-in while we populate the lists.
                if (value is not null && _instanceBrowser is not null)
                {
                    _ = RefreshAllInstanceTabsAsync(value);
                }
                else
                {
                    InstanceScreenshots = Array.Empty<ScreenshotEntry>();
                    InstanceWorlds = Array.Empty<WorldEntry>();
                    InstanceServers = Array.Empty<ServerListEntry>();
                    InstanceResourcePacks?.Clear();
                    InstanceShaderPacks?.Clear();
                    InstanceDataPacks?.Clear();
                }
                // Crash reports use a separate listener; refresh independently of the regular browser.
                if (value is not null && _crashReportListener is not null)
                {
                    _ = RefreshInstanceCrashReportsAsync();
                }
                else
                {
                    InstanceCrashReports = Array.Empty<CrashReport>();
                    SelectedCrashReport = null;
                }
                OnPropertyChanged(nameof(CanManageInstanceMods));
                InstallModCommand?.RaiseCanExecuteChanged();
                RefreshInstalledModsCommand?.RaiseCanExecuteChanged();
                ToggleInstalledModCommand?.RaiseCanExecuteChanged();
                RemoveInstalledModCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when an instance is selected - drives the visibility of the detail tab panel.</summary>
    public bool HasSelectedInstance => _selectedInstance is not null;

    /// <summary>Screenshots under the selected instance's <c>screenshots/</c>, newest first.</summary>
    public IReadOnlyList<ScreenshotEntry> InstanceScreenshots
    {
        get => _instanceScreenshots;
        private set => SetField(ref _instanceScreenshots, value);
    }

    /// <summary>Saved worlds under the selected instance's <c>saves/</c>.</summary>
    public IReadOnlyList<WorldEntry> InstanceWorlds
    {
        get => _instanceWorlds;
        private set => SetField(ref _instanceWorlds, value);
    }

    /// <summary>Multiplayer entries from the selected instance's <c>servers.dat</c>.</summary>
    public IReadOnlyList<ServerListEntry> InstanceServers
    {
        get => _instanceServers;
        private set => SetField(ref _instanceServers, value);
    }

    /// <summary>Resource packs (zip / zip.disabled) under <c>&lt;gameDir&gt;/resourcepacks/</c>.</summary>
    public ObservableCollection<ResourcePackEntry> InstanceResourcePacks { get; }

    /// <summary>Shader packs under <c>&lt;gameDir&gt;/shaderpacks/</c>.</summary>
    public ObservableCollection<ShaderPackEntry> InstanceShaderPacks { get; }

    /// <summary>Data packs under <c>&lt;gameDir&gt;/saves/&lt;world&gt;/datapacks/</c>, grouped by world (XAML sees the flat list).</summary>
    public ObservableCollection<DataPackEntry> InstanceDataPacks { get; }

    /// <summary>Crash reports parsed from <c>&lt;gameDir&gt;/crash-reports/</c>, newest first.</summary>
    public IReadOnlyList<CrashReport> InstanceCrashReports
    {
        get => _instanceCrashReports;
        private set
        {
            if (SetField(ref _instanceCrashReports, value))
            {
                OnPropertyChanged(nameof(HasInstanceCrashReports));
                // Clear selection if it no longer exists in the new list.
                if (SelectedCrashReport is not null && !value.Contains(SelectedCrashReport))
                    SelectedCrashReport = value.Count > 0 ? value[0] : null;
                else if (SelectedCrashReport is null && value.Count > 0)
                    SelectedCrashReport = value[0];
            }
        }
    }

    /// <summary>True when the selected instance has at least one crash report to show.</summary>
    public bool HasInstanceCrashReports => _instanceCrashReports.Count > 0;

    /// <summary>The crash report currently focused in the Crashes tab. Drives the viewer pane + mod-search button strip.</summary>
    public CrashReport? SelectedCrashReport
    {
        get => _selectedCrashReport;
        set
        {
            if (SetField(ref _selectedCrashReport, value))
            {
                OnPropertyChanged(nameof(HasSelectedCrashReport));
                OnPropertyChanged(nameof(SelectedCrashReportSuspects));
                OpenCrashReportInBrowserCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when a crash report is selected - drives the visibility of the viewer pane.</summary>
    public bool HasSelectedCrashReport => _selectedCrashReport is not null;

    /// <summary>The suspect frames of the selected crash report, or empty when nothing is selected. Drives the button strip.</summary>
    public IReadOnlyList<CrashFrame> SelectedCrashReportSuspects =>
        _selectedCrashReport?.SuspectedMods ?? Array.Empty<CrashFrame>();

    /// <summary>Active sub-tab in the per-instance detail panel.</summary>
    public InstanceDetailTab SelectedInstanceTab
    {
        get => _selectedInstanceTab;
        set
        {
            if (SetField(ref _selectedInstanceTab, value))
            {
                OnPropertyChanged(nameof(IsScreenshotsTabSelected));
                OnPropertyChanged(nameof(IsWorldsTabSelected));
                OnPropertyChanged(nameof(IsInstanceServersTabSelected));
                OnPropertyChanged(nameof(IsCrashesTabSelected));
                OnPropertyChanged(nameof(IsResourcePacksTabSelected));
                OnPropertyChanged(nameof(IsShaderPacksTabSelected));
                OnPropertyChanged(nameof(IsDataPacksTabSelected));
            }
        }
    }

    public bool IsScreenshotsTabSelected => SelectedInstanceTab == InstanceDetailTab.Screenshots;
    public bool IsWorldsTabSelected => SelectedInstanceTab == InstanceDetailTab.Worlds;
    public bool IsInstanceServersTabSelected => SelectedInstanceTab == InstanceDetailTab.Servers;
    public bool IsCrashesTabSelected => SelectedInstanceTab == InstanceDetailTab.Crashes;
    public bool IsResourcePacksTabSelected => SelectedInstanceTab == InstanceDetailTab.ResourcePacks;
    public bool IsShaderPacksTabSelected => SelectedInstanceTab == InstanceDetailTab.ShaderPacks;
    public bool IsDataPacksTabSelected => SelectedInstanceTab == InstanceDetailTab.DataPacks;

    // ---- Settings ----

    /// <summary>JVM minimum heap (-Xms) in MiB.</summary>
    public int MinMemoryMb
    {
        get => _minMemoryMb;
        set
        {
            // Clamp min <= max
            var clamped = Math.Clamp(value, 256, _maxAllowedMemoryMb);
            if (clamped > _maxMemoryMb) clamped = _maxMemoryMb;
            SetField(ref _minMemoryMb, clamped);
        }
    }

    /// <summary>JVM maximum heap (-Xmx) in MiB.</summary>
    public int MaxMemoryMb
    {
        get => _maxMemoryMb;
        set
        {
            var clamped = Math.Clamp(value, 512, _maxAllowedMemoryMb);
            if (clamped < _minMemoryMb) _minMemoryMb = clamped;
            if (SetField(ref _maxMemoryMb, clamped))
                OnPropertyChanged(nameof(MinMemoryMb));
        }
    }

    /// <summary>Recommended ceiling for the memory sliders (~75% of detected system RAM, capped at 16 GB).</summary>
    public int MaxAllowedMemoryMb => _maxAllowedMemoryMb;

    /// <summary>Extra JVM args appended after heap flags.</summary>
    public string JvmArguments
    {
        get => _jvmArguments;
        set => SetField(ref _jvmArguments, value ?? string.Empty);
    }

    /// <summary>Override the <c>.minecraft</c> root. Empty string = OS default.</summary>
    public string GameDirectoryOverride
    {
        get => _gameDirectoryOverride;
        set => SetField(ref _gameDirectoryOverride, value ?? string.Empty);
    }

    /// <summary>Override the Java executable. Empty string = CmlLib's own auto-detect.</summary>
    public string JavaExecutableOverride
    {
        get => _javaExecutableOverride;
        set => SetField(ref _javaExecutableOverride, value ?? string.Empty);
    }

    /// <summary>If true, the launcher window stays open after the game starts.</summary>
    public bool KeepLauncherOpen
    {
        get => _keepLauncherOpen;
        set => SetField(ref _keepLauncherOpen, value);
    }

    /// <summary>If true, the in-app log mirrors the game's stdout/stderr.</summary>
    public bool ShowGameLog
    {
        get => _showGameLog;
        set => SetField(ref _showGameLog, value);
    }

    /// <summary>
    /// True when the left sidebar is collapsed to icons-only. Persists immediately through the
    /// settings store (fire-and-forget) so the next launch starts in the user's preferred mode.
    /// Setter also raises <see cref="SidebarWidth"/> so the bound Border width transitions.
    /// </summary>
    public bool IsSidebarCollapsed
    {
        get => _sidebarCollapsed;
        set
        {
            if (SetField(ref _sidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(AreSidebarLabelsVisible));
                // Fire-and-forget persist; the file store is small and atomic. We deliberately
                // don't await here so the UI toggle stays snappy and the transition runs even
                // if disk I/O blips.
                _ = PersistSidebarCollapsedAsync(value);
            }
        }
    }

    /// <summary>Live sidebar width in DIPs, driven by <see cref="IsSidebarCollapsed"/>. Bound to a Border in MainWindow.</summary>
    public double SidebarWidth => _sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;

    /// <summary>True when the sidebar labels should render (i.e. the sidebar is expanded).</summary>
    public bool AreSidebarLabelsVisible => !_sidebarCollapsed;

    private async Task PersistSidebarCollapsedAsync(bool collapsed)
    {
        if (_settingsStore is null) return;
        try
        {
            // Preserve every other field by loading first, then writing the patched record.
            var current = await _settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            await _settingsStore.SaveAsync(current with { SidebarCollapsed = collapsed }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not persist SidebarCollapsed={collapsed}: {ex.Message}");
        }
    }

    private Task ToggleSidebarAsync()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        return Task.CompletedTask;
    }

    /// <summary>
    /// v0.32.1 (T-startup-perf): async wrapper that reads persisted <see cref="LauncherSettings"/>
    /// from disk (if a store was injected) and pushes the values into the view-model. Failures
    /// fall back to defaults so a corrupt settings.json never blocks startup. Public so the
    /// View / tests can call it explicitly after deferring it from the ctor.
    /// </summary>
    public async Task ApplyLoadedSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsStore is null) return;
        try
        {
            var loaded = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);
            ApplySettings(loaded);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not load persisted settings ({ex.Message}); keeping defaults.");
        }
    }

    private void ApplySettings(LauncherSettings s)
    {
        _maxMemoryMb = Math.Clamp(s.MaximumRamMb, 512, _maxAllowedMemoryMb);
        _minMemoryMb = Math.Clamp(s.MinimumRamMb, 256, _maxMemoryMb);
        _jvmArguments = s.JvmArguments ?? string.Empty;
        _gameDirectoryOverride = s.GameDirectory ?? string.Empty;
        _javaExecutableOverride = s.JavaExecutable ?? string.Empty;
        _keepLauncherOpen = s.KeepLauncherOpen;
        _showGameLog = s.ShowGameLog;
        _sidebarCollapsed = s.SidebarCollapsed;
        _autoUpdateCheckEnabled = s.AutoUpdateCheckEnabled;
        _autoBackupBeforeLaunch = s.AutoBackupBeforeLaunch;
        _autoBackupKeepLatest = s.AutoBackupKeepLatest;
        _locale = s.Locale;
        _curseForgeApiKey = s.CurseForgeApiKey ?? string.Empty;
    }

    /// <summary>Snapshot the current VM state as a persistable <see cref="LauncherSettings"/>.</summary>
    public LauncherSettings BuildSettings() => new()
    {
        MinimumRamMb = _minMemoryMb,
        MaximumRamMb = _maxMemoryMb,
        JvmArguments = _jvmArguments,
        GameDirectory = string.IsNullOrWhiteSpace(_gameDirectoryOverride) ? null : _gameDirectoryOverride,
        JavaExecutable = string.IsNullOrWhiteSpace(_javaExecutableOverride) ? null : _javaExecutableOverride,
        KeepLauncherOpen = _keepLauncherOpen,
        ShowGameLog = _showGameLog,
        SidebarCollapsed = _sidebarCollapsed,
        AutoUpdateCheckEnabled = _autoUpdateCheckEnabled,
        AutoBackupBeforeLaunch = _autoBackupBeforeLaunch,
        AutoBackupKeepLatest = _autoBackupKeepLatest,
        Locale = string.IsNullOrWhiteSpace(_locale) ? null : _locale,
        CurseForgeApiKey = _curseForgeApiKey ?? string.Empty,
    };

    /// <summary>
    /// True when the user has stored a CurseForge API key. Drives the Mods page empty-state
    /// (hide the panel when configured) and the Settings card status row. Updated on
    /// settings load and whenever the onboarding dialog persists a fresh value.
    /// </summary>
    public bool HasCurseForgeKey => !string.IsNullOrWhiteSpace(_curseForgeApiKey);

    /// <summary>
    /// True when the user has clicked the CurseForge source toggle on the Mods page AND no
    /// API key is configured yet. Drives the friendly empty-state panel that points to the
    /// onboarding dialog.
    /// </summary>
    public bool ShouldShowCurseForgeEmptyState => IsCurseForgeSelected && !HasCurseForgeKey;

    /// <summary>The currently-stored CurseForge API key (read-only; mutation goes through the dialog).</summary>
    public string CurseForgeApiKey => _curseForgeApiKey ?? string.Empty;

    /// <summary>If true, every launch zips the instance's worlds into <c>&lt;gameDir&gt;/backups/</c> first.</summary>
    public bool AutoBackupBeforeLaunch
    {
        get => _autoBackupBeforeLaunch;
        set => SetField(ref _autoBackupBeforeLaunch, value);
    }

    /// <summary>How many world backups to retain per world after pre-launch pruning. 0 = keep everything.</summary>
    public int AutoBackupKeepLatest
    {
        get => _autoBackupKeepLatest;
        set => SetField(ref _autoBackupKeepLatest, Math.Max(0, value));
    }

    /// <summary>
    /// Currently-selected option in the Settings page "Display language" dropdown. <c>null</c>
    /// means "use system default" (which falls back to <see cref="CultureInfo.CurrentUICulture"/>).
    /// Setting this asynchronously notifies <see cref="ILocalizationService"/>; only persisted
    /// when the user clicks <em>Save settings</em>.
    /// </summary>
    public string? Locale
    {
        get => _locale;
        set
        {
            // Normalize empty string -> null (the dropdown's "use system default" option binds null).
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetField(ref _locale, normalized) && _localizationService is not null)
            {
                // Fire-and-forget the culture switch. The service is in-memory; the await is a
                // synchronous shim. We swallow the awaiter because the property setter must stay
                // synchronous for two-way bindings.
                var target = normalized ?? CultureInfo.CurrentUICulture.Name;
                _ = _localizationService.SetCultureAsync(target, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Locale options shown in the Settings page dropdown. The first entry is always
    /// <c>(null, "Use system default")</c>; the rest mirror
    /// <see cref="ILocalizationService.AvailableCultures"/>.
    /// </summary>
    public IReadOnlyList<LocaleOption> AvailableLocales =>
        BuildAvailableLocales(_localizationService);

    private static IReadOnlyList<LocaleOption> BuildAvailableLocales(ILocalizationService? svc)
    {
        var options = new List<LocaleOption>
        {
            new LocaleOption(null, Strings.Settings_LanguageUseSystemDefault),
        };
        if (svc is null) return options;
        foreach (var c in svc.AvailableCultures)
        {
            string display;
            try { display = CultureInfo.GetCultureInfo(c).NativeName; }
            catch { display = c; }
            options.Add(new LocaleOption(c, display));
        }
        return options;
    }

    /// <summary>Currently-selected profile on the Installations page. Picking a profile pre-fills the launch context.</summary>
    public LauncherProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value) && value is not null)
            {
                // Cherry-pick the most useful per-profile defaults into the home page so
                // the user can launch with "their" profile settings without retyping.
                if (!string.IsNullOrEmpty(value.LastVersionId))
                {
                    // Try to surface the matching installed version (if any) and pre-select it.
                    var match = FindInstalledById(value.LastVersionId);
                    if (match is not null) SelectedInstalledVersion = match;
                }
                Append($"Using profile '{value.Name}' (version {value.LastVersionId ?? "-"}).");
            }
        }
    }

    private InstalledVersion? FindInstalledById(string id)
    {
        foreach (var v in InstalledVersions)
            if (string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    /// <summary>Selection in the manifest dropdown.</summary>
    public VersionMetadata? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetField(ref _selectedVersion, value))
                LaunchCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Selection in the installed-versions list.</summary>
    public InstalledVersion? SelectedInstalledVersion
    {
        get => _selectedInstalledVersion;
        set
        {
            if (SetField(ref _selectedInstalledVersion, value))
                LaunchCommand.RaiseCanExecuteChanged();
        }
    }

    // ---- Log / busy ----

    /// <summary>Multi-line log displayed in the window.</summary>
    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    /// <summary>True while an async command is running. Disables every button.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshVersionsCommand.RaiseCanExecuteChanged();
                RefreshInstalledVersionsCommand.RaiseCanExecuteChanged();
                RefreshProfilesCommand.RaiseCanExecuteChanged();
                RefreshServersCommand.RaiseCanExecuteChanged();
                RefreshServerPingsCommand.RaiseCanExecuteChanged();
                RefreshNewsCommand.RaiseCanExecuteChanged();
                RefreshInstancesCommand.RaiseCanExecuteChanged();
                DeleteInstanceCommand.RaiseCanExecuteChanged();
                SaveSettingsCommand.RaiseCanExecuteChanged();
                LaunchCommand.RaiseCanExecuteChanged();
                SignInMicrosoftCommand.RaiseCanExecuteChanged();
                SignOutCommand.RaiseCanExecuteChanged();
                RefreshLogFileCommand.RaiseCanExecuteChanged();
                RefreshInstanceScreenshotsCommand.RaiseCanExecuteChanged();
                RefreshInstanceWorldsCommand.RaiseCanExecuteChanged();
                RefreshInstanceServersCommand.RaiseCanExecuteChanged();
                RefreshInstanceCrashReportsCommand.RaiseCanExecuteChanged();
                RefreshInstanceResourcePacksCommand?.RaiseCanExecuteChanged();
                RefreshInstanceShaderPacksCommand?.RaiseCanExecuteChanged();
                RefreshInstanceDataPacksCommand?.RaiseCanExecuteChanged();
                ToggleResourcePackCommand?.RaiseCanExecuteChanged();
                ToggleShaderPackCommand?.RaiseCanExecuteChanged();
                ToggleDataPackCommand?.RaiseCanExecuteChanged();
                RemoveResourcePackCommand?.RaiseCanExecuteChanged();
                RemoveShaderPackCommand?.RaiseCanExecuteChanged();
                RemoveDataPackCommand?.RaiseCanExecuteChanged();
                UploadSkinCommand.RaiseCanExecuteChanged();
                SetActiveCapeCommand.RaiseCanExecuteChanged();
                ClearActiveCapeCommand.RaiseCanExecuteChanged();
                ReapplyHistoricSkinCommand.RaiseCanExecuteChanged();
                ApplyBrowsedSkinCommand.RaiseCanExecuteChanged();
                SearchModsCommand.RaiseCanExecuteChanged();
                InstallModCommand.RaiseCanExecuteChanged();
                RefreshInstalledModsCommand.RaiseCanExecuteChanged();
                ToggleInstalledModCommand.RaiseCanExecuteChanged();
                RemoveInstalledModCommand.RaiseCanExecuteChanged();
                SwitchAccountCommand.RaiseCanExecuteChanged();
                AddAccountCommand.RaiseCanExecuteChanged();
                RemoveAccountCommand.RaiseCanExecuteChanged();
                CheckForUpdatesCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    // ---- Sidebar ----

    /// <summary>Currently-selected sidebar item. Bindings on the content panels gate their visibility on this.</summary>
    public NavSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetField(ref _selectedSection, value))
            {
                // Each "Is*Selected" guard backs an x:Bind-style visibility in MainWindow.axaml.
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsInstallationsSelected));
                OnPropertyChanged(nameof(IsSkinsSelected));
                OnPropertyChanged(nameof(IsServersSelected));
                OnPropertyChanged(nameof(IsHeadlessServersSelected));
                OnPropertyChanged(nameof(IsModsSelected));
                OnPropertyChanged(nameof(IsNewsSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
                OnPropertyChanged(nameof(IsLogsSelected));

                // Auto-load today's log file the first time the user opens the Logs page so
                // it isn't blank. The Refresh button re-reads it on demand after that.
                if (value == NavSection.Logs)
                    _ = LoadTodaysLogAsync();

                // Auto-populate the headless server list the first time the user opens that page.
                if (value == NavSection.HeadlessServers && _headlessServerStore is not null)
                    _ = RefreshHeadlessServersAsync();

                // T-namemc (v0.32.1): warm up the community gallery the first time the user
                // opens the Skins page. Subsequent visits keep the existing list (or the user
                // hits the explicit Refresh / search box).
                if (value == NavSection.Skins && _skinBrowser is not null && BrowsedSkins.Count == 0)
                    _ = RefreshTrendingBrowsedSkinsAsync();
            }
        }
    }

    public bool IsHomeSelected => SelectedSection == NavSection.Home;
    public bool IsInstallationsSelected => SelectedSection == NavSection.Installations;
    public bool IsSkinsSelected => SelectedSection == NavSection.Skins;
    public bool IsServersSelected => SelectedSection == NavSection.Servers;
    public bool IsHeadlessServersSelected => SelectedSection == NavSection.HeadlessServers;
    public bool IsModsSelected => SelectedSection == NavSection.Mods;
    public bool IsNewsSelected => SelectedSection == NavSection.News;
    public bool IsSettingsSelected => SelectedSection == NavSection.Settings;
    public bool IsLogsSelected => SelectedSection == NavSection.Logs;

    // ---- Mods page ----

    /// <summary>Search results from the currently selected mod source.</summary>
    public ObservableCollection<Mod> ModSearchResults { get; }

    /// <summary>Mods installed in the selected instance's <c>mods/</c> directory.</summary>
    public ObservableCollection<LocalMod> InstalledMods { get; }

    /// <summary>Search term entered in the Mods page top row.</summary>
    public string ModSearchTerm
    {
        get => _modSearchTerm;
        set => SetField(ref _modSearchTerm, value ?? string.Empty);
    }

    /// <summary>Currently-active mod source. Drives which repository serves search results.</summary>
    public ModSource SelectedModSource
    {
        get => _selectedModSource;
        set
        {
            if (SetField(ref _selectedModSource, value))
            {
                OnPropertyChanged(nameof(IsModrinthSelected));
                OnPropertyChanged(nameof(IsCurseForgeSelected));
                OnPropertyChanged(nameof(ShouldShowCurseForgeEmptyState));
                SearchModsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsModrinthSelected => SelectedModSource == ModSource.Modrinth;
    public bool IsCurseForgeSelected => SelectedModSource == ModSource.CurseForge;

    /// <summary>Selected card in the search-results list.</summary>
    public Mod? SelectedModSearchResult
    {
        get => _selectedModSearchResult;
        set
        {
            if (SetField(ref _selectedModSearchResult, value))
                InstallModCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Selected entry in the right-hand installed-mods list.</summary>
    public LocalMod? SelectedInstalledMod
    {
        get => _selectedInstalledMod;
        set
        {
            if (SetField(ref _selectedInstalledMod, value))
            {
                ToggleInstalledModCommand.RaiseCanExecuteChanged();
                RemoveInstalledModCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when the Mods page has a selected instance + a manager - enables the right-hand pane.</summary>
    public bool CanManageInstanceMods => SelectedInstance is not null && _instanceModManager is not null;

    // ---- Commands ----

    public AsyncRelayCommand RefreshVersionsCommand { get; }
    public AsyncRelayCommand RefreshInstalledVersionsCommand { get; }
    public AsyncRelayCommand RefreshProfilesCommand { get; }
    public AsyncRelayCommand RefreshServersCommand { get; }
    public AsyncRelayCommand RefreshServerPingsCommand { get; }
    public AsyncRelayCommand RefreshNewsCommand { get; }
    public AsyncRelayCommand RefreshInstancesCommand { get; }
    public AsyncRelayCommand DeleteInstanceCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand SignInMicrosoftCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }
    public AsyncRelayCommand RefreshLogFileCommand { get; }
    public AsyncRelayCommand RefreshInstanceScreenshotsCommand { get; }
    public AsyncRelayCommand RefreshInstanceWorldsCommand { get; }
    public AsyncRelayCommand RefreshInstanceServersCommand { get; }
    public AsyncRelayCommand RefreshInstanceCrashReportsCommand { get; }
    public AsyncRelayCommand RefreshInstanceResourcePacksCommand { get; }
    public AsyncRelayCommand RefreshInstanceShaderPacksCommand { get; }
    public AsyncRelayCommand RefreshInstanceDataPacksCommand { get; }
    /// <summary>Toggle a resource pack enable/disable by filename (renames <c>.zip</c> &lt;-&gt; <c>.zip.disabled</c>).</summary>
    public AsyncParameterRelayCommand<string> ToggleResourcePackCommand { get; }
    /// <summary>Toggle a shader pack by filename.</summary>
    public AsyncParameterRelayCommand<string> ToggleShaderPackCommand { get; }
    /// <summary>Toggle a data pack. Needs the full entry (we need both filename and the owning world folder).</summary>
    public AsyncParameterRelayCommand<DataPackEntry> ToggleDataPackCommand { get; }
    public AsyncParameterRelayCommand<string> RemoveResourcePackCommand { get; }
    public AsyncParameterRelayCommand<string> RemoveShaderPackCommand { get; }
    public AsyncParameterRelayCommand<DataPackEntry> RemoveDataPackCommand { get; }
    public AsyncRelayCommand OpenCrashReportInBrowserCommand { get; }
    public AsyncRelayCommand UploadSkinCommand { get; }
    public AsyncRelayCommand SetActiveCapeCommand { get; }
    public AsyncRelayCommand ClearActiveCapeCommand { get; }
    public AsyncRelayCommand<SkinHistoryEntry> ReapplyHistoricSkinCommand { get; }
    /// <summary>Fetch the trending gallery from the configured <see cref="ISkinBrowser"/> (NameMC).</summary>
    public AsyncRelayCommand RefreshTrendingSkinsCommand { get; }
    /// <summary>Search the configured <see cref="ISkinBrowser"/> using <see cref="SkinSearchText"/>.</summary>
    public AsyncRelayCommand SearchSkinsCommand { get; }
    /// <summary>Download the picked browsed skin and apply it to the signed-in Mojang account.</summary>
    public AsyncRelayCommand<BrowsedSkin> ApplyBrowsedSkinCommand { get; }
    public AsyncRelayCommand SearchModsCommand { get; }
    public AsyncRelayCommand InstallModCommand { get; }
    public AsyncRelayCommand RefreshInstalledModsCommand { get; }
    public AsyncRelayCommand ToggleInstalledModCommand { get; }
    public AsyncRelayCommand RemoveInstalledModCommand { get; }
    public AsyncRelayCommand OpenCurseForgeOnboardingCommand { get; }
    public AsyncParameterRelayCommand<AccountWithBitmap> SwitchAccountCommand { get; }
    public AsyncRelayCommand AddAccountCommand { get; }
    public AsyncParameterRelayCommand<Account> RemoveAccountCommand { get; }
    public AsyncRelayCommand ExportInstanceCommand { get; }
    public AsyncRelayCommand ImportInstanceCommand { get; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task RefreshVersionsAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_LoadingVersionManifest);
            var versions = await _service.ListVersionsAsync(CancellationToken.None);

            AvailableVersions.Clear();
            foreach (var v in versions)
                AvailableVersions.Add(v);

            // Rebuild the filtered view (and re-evaluate the SelectedVersion fallback)
            // now that the source list has new entries.
            ApplyVersionFilter();

            Append(string.Format(Strings.Log_LoadedNVersions, versions.Count));
            _logger.Info($"Version manifest refreshed ({versions.Count} entries).");
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
            _logger.Warn($"RefreshVersions surfaced LauncherException to UI: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Populate every observable list (installed versions, profiles, servers, news, manifest)
    /// on startup so the user lands on a fully-rendered launcher without having to click five
    /// "Refresh" buttons. Runs the refreshes in parallel for fast first-paint; surfaces failures
    /// through the existing log/Append path. Safe to call once on construction.
    /// </summary>
    private bool CanLaunch()
        => !IsBusy && (SelectedInstance is not null || SelectedInstalledVersion is not null || SelectedVersion is not null);

    public async Task RunStartupRefreshesAsync()
    {
        // v0.32.1 (T-startup-perf): settings load was previously synchronous in the ctor; now
        // it runs once here, asynchronously, before any refresh kicks off. The VM was
        // initialised with default LauncherSettings so bound controls already had something
        // to render; this overwrite brings in the user's persisted memory / jvm / locale prefs.
        await ApplyLoadedSettingsAsync().ConfigureAwait(true);

        // Announce idle to Discord (no-op when RPC is disabled or Discord isn't running).
        // v0.32.1: presence is now a DeferredPresenceService proxy, so SetIdle records the
        // call and replays it once Discord IPC finishes handshaking on its background thread.
        try { _presence.SetIdle(); }
        catch (Exception ex) { _logger.Warn($"Presence SetIdle failed: {ex.Message}"); }

        // v0.32.1 (T-startup-perf): the gating WhenAll now only includes the work that the user
        // visibly needs to see populated on first-paint. Anything network-bound that the user
        // doesn't immediately interact with (news, MSAL silent sign-in, update probe) is moved
        // to the deferred section below so the [startup] log line shrinks below 4 s.
        Append(Strings.Log_AutoRefreshingOnStartup);
        await Task.WhenAll(
            TimedAsync("versions", RefreshVersionsAsync),
            TimedAsync("instances", RefreshInstancesAsync),
            TimedAsync("profiles", RefreshProfilesAsync),
            TimedAsync("servers", RefreshServersAsync),
            TimedAsync("accounts", () => RefreshAccountsAsync(CancellationToken.None)),
            TimedAsync("skin-history", ReloadHistoryAsync)).ConfigureAwait(true);

        // Emit the gating-work timeline immediately - this is the "how long did the user wait
        // for visible content" number the perf task is graded on.
        StartupTimeline.ReportTo(_logger);

        // -- Deferred work below this line --
        // Run news, silent MSAL sign-in, and the update probe in parallel as fire-and-forget
        // background work. Their completion timings land in a separate [startup-deferred] log
        // line so we can still tell which one is dragging without polluting [startup] TOTAL.
        StartupTimeline.BeginDeferred();
        _ = Task.Run(RunDeferredStartupWorkAsync);
    }

    /// <summary>
    /// v0.32.1: the deferred half of startup. Runs after first-paint and after the [startup]
    /// log line has already been emitted. Failures are swallowed (already logged inside the
    /// individual refresh methods) so a slow GitHub probe / dead network can't tear the UI down.
    /// </summary>
    private async Task RunDeferredStartupWorkAsync()
    {
        // News fetch was previously in the gating WhenAll. It uses a 1-hour disk cache so the
        // cold path is the only one that pays network. Push it to deferred so the first 1.5 s
        // of UI aren't gated on launchercontent.mojang.com.
        var newsSw = Stopwatch.StartNew();
        try { await RefreshNewsAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.Warn($"Deferred news refresh failed: {ex.Message}"); }
        newsSw.Stop();
        StartupTimeline.RecordDeferred("news", newsSw.ElapsedMilliseconds);

        // Silent Microsoft sign-in. Wait 2 s so the user's first interaction isn't competing
        // with an MSAL token round-trip on the UI thread. The 2 s constant is the task brief.
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var msAuthSw = Stopwatch.StartNew();
        if (_microsoftAuth is { HasCachedAccount: true })
        {
            try
            {
                Append(Strings.Log_SilentMsSignInAttempt);
                if (_activeAccount is { IsOffline: false, Id.Length: > 0 } active)
                    CurrentSession = await _microsoftAuth.SignInSilentlyAsync(active.Id, CancellationToken.None).ConfigureAwait(false);
                else
                    CurrentSession = await _microsoftAuth.SignInSilentlyAsync(CancellationToken.None).ConfigureAwait(false);
                Append(string.Format(Strings.Log_AutoSignedInAs, CurrentSession!.Username));
                // Populate OwnedSkins + OwnedCapes from the Mojang profile so the Skins
                // page is fully populated by the time the user opens it.
                await TryRefreshProfileAsync(CurrentSession.AccessToken).ConfigureAwait(false);
                // The post-sign-in store update may have changed the account list / active id.
                await RefreshAccountsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Refresh token expired or no cache - leave the user signed out, they can
                // click Sign in manually to trigger the device-code flow.
                Append(string.Format(Strings.Log_SilentSignInSkipped, ex.Message));
            }
        }
        msAuthSw.Stop();
        StartupTimeline.RecordDeferred("ms-auth", msAuthSw.ElapsedMilliseconds);

        // T16: launcher update probe. Best-effort. Disabled toggles or a missing checker
        // silently no-op; transport failures are swallowed inside the checker.
        var updateSw = Stopwatch.StartNew();
        if (_autoUpdateCheckEnabled && _updateChecker is not null)
        {
            try
            {
                var info = await _updateChecker.CheckAsync(CurrentLauncherVersion, CancellationToken.None).ConfigureAwait(false);
                if (info is not null)
                {
                    AvailableUpdate = info;
                    Append($"A newer launcher version is available: v{info.LatestVersion}");
                }
            }
            catch (Exception ex)
            {
                // The checker contract says it shouldn't throw; defend against future drift anyway.
                _logger.Warn($"Update check failed: {ex.Message}");
            }
        }
        updateSw.Stop();
        StartupTimeline.RecordDeferred("update-check", updateSw.ElapsedMilliseconds);

        // Emit the [startup-deferred] log line. TOTAL is the sum of the three labels above;
        // any 2 s Task.Delay between them is intentional and not included.
        StartupTimeline.ReportDeferredTo(_logger);
    }

    /// <summary>
    /// T18: wrap a refresh step in a stopwatch + StartupTimeline.Record call. Any exception
    /// from the step is caught here so one slow/broken refresh can't fail the whole Task.WhenAll;
    /// the inner Refresh* methods already surface their own [error] line via Append.
    /// </summary>
    private static async Task TimedAsync(string label, Func<Task> step)
    {
        var sw = Stopwatch.StartNew();
        try { await step().ConfigureAwait(true); }
        catch
        {
            // Inner refresh methods are expected to handle their own LauncherException. Anything
            // that still escapes here is a bug worth surfacing - but we still want the timeline
            // line to be emitted, so swallow it and let the inner method's Append/log carry the
            // error. (Re-raising would tear the whole Task.WhenAll down for one bad step.)
        }
        finally
        {
            sw.Stop();
            StartupTimeline.Record(label, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// The currently running launcher version, read at first access from the executing
    /// assembly's metadata. The assembly version is set by MSBuild from the repo-root
    /// <c>/VERSION</c> file via <c>Directory.Build.props</c>, so the single source of
    /// truth lives in one place: edit <c>/VERSION</c>, build, and every consumer
    /// (this property, the update-checker probe, the sidebar's version label) picks
    /// up the new number automatically.
    /// </summary>
    public static string CurrentLauncherVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Pre-formatted version label rendered in the sidebar footer
    /// (e.g. <c>"v0.32.0"</c>). Bound from <c>MainWindow.axaml</c> instead of the
    /// older static <c>Sidebar.ParityBuildLabel</c> resource so the displayed version
    /// follows <see cref="CurrentLauncherVersion"/> automatically.
    /// </summary>
    public string LauncherVersionLabel => $"v{CurrentLauncherVersion}";

    /// <summary>
    /// Re-read the cached-accounts roster (MSAL + offline placeholders), reconcile our
    /// observable list, and update <see cref="ActiveAccount"/>. Safe to call repeatedly.
    /// </summary>
    public async Task RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        if (_microsoftAuth is null && _accountStore is null) return;

        IReadOnlyList<Account> list = Array.Empty<Account>();
        try
        {
            list = _microsoftAuth is not null
                ? await _microsoftAuth.ListCachedAsync(cancellationToken).ConfigureAwait(false)
                : await _accountStore!.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"RefreshAccounts failed: {ex.Message}");
        }

        // Bug 2 (v0.32.0): the flyout used to show the same account twice when the MSAL
        // cache and the file account store both reported it (e.g. the store's row had a
        // slightly different Id casing or a stale Uuid). Group by Id and keep the most
        // recently used entry as the canonical row, so the multi-account switcher lists
        // every distinct account exactly once.
        var deduped = list
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.LastUsedAt).First())
            .OrderByDescending(a => a.LastUsedAt)
            .ToList();

        // v0.32.2 (T-flyout-avatar): fetch the head face for each non-offline account so the
        // flyout row mini-avatar shows the real skin, not the bundled Steve. The fetch hits
        // the disk cache first (6 h TTL) so re-opening the launcher is instant. Failures
        // are swallowed; rows with null HeadBitmap render the Steve fallback in XAML.
        var projections = new List<AccountWithBitmap>(deduped.Count);
        foreach (var account in deduped)
        {
            Bitmap? head = null;
            if (!account.IsOffline && !string.IsNullOrWhiteSpace(account.Uuid))
            {
                try
                {
                    head = await TryLoadAccountHeadAsync(account.Uuid, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Could not load avatar for '{account.Username}' ({account.Uuid}): {ex.Message}");
                }
            }
            projections.Add(new AccountWithBitmap { Account = account, HeadBitmap = head });
        }

        Accounts.Clear();
        foreach (var p in projections) Accounts.Add(p);

        Account? active = null;
        try
        {
            active = _accountStore is null
                ? null
                : await _accountStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"GetActive failed: {ex.Message}");
        }
        // Fall back to the most-recent account when the store didn't pin one (single-account upgrade path).
        active ??= Accounts.FirstOrDefault()?.Account;
        ActiveAccount = active;
    }

    /// <summary>
    /// Resolve the head-face <see cref="Bitmap"/> for an account UUID: read from the per-uuid
    /// PNG cache when fresh, otherwise call the Mojang fetcher, crop the 8x8 face with
    /// <see cref="Skin2DHeadTypeA.MakeHeadImage"/> (the same helper the header chip uses), then
    /// persist the result. Returns <c>null</c> on any failure - the flyout row falls back to
    /// the bundled Steve face in that case.
    /// </summary>
    /// <remarks>
    /// Internal-visible so the test project can spy on it; the production view-model wires it
    /// through the injected <see cref="IPlayerSkinFetcher"/> only. Without that dependency the
    /// method short-circuits to <c>null</c> so unit tests without a fetcher keep working.
    /// </remarks>
    internal async Task<Bitmap?> TryLoadAccountHeadAsync(string uuid, CancellationToken cancellationToken)
    {
        if (_skinFetcher is null || string.IsNullOrWhiteSpace(uuid)) return null;

        var trimmed = uuid.Replace("-", string.Empty);
        if (trimmed.Length != 32) return null;

        // Cache hit: the cropped head PNG is small and re-decoding is cheap; we still avoid
        // the round-trip to Mojang + the SkiaSharp crop pipeline on every flyout open.
        if (_accountHeadCache is not null)
        {
            var cached = await _accountHeadCache
                .TryReadAsync(AccountHeadCacheKey(trimmed), maxAge: TimeSpan.FromHours(6), cancellationToken)
                .ConfigureAwait(false);
            if (cached is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(cached);
                    return new Bitmap(ms);
                }
                catch
                {
                    // Bad cache entry - fall through to network fetch.
                }
            }
        }

        var info = await _skinFetcher.FetchAsync(trimmed, cancellationToken).ConfigureAwait(false);
        if (info is null || info.SkinPng.Length == 0) return null;

        try
        {
            using var sk = SKBitmap.Decode(info.SkinPng);
            if (sk is null) return null;
            using var head = Skin2DHeadTypeA.MakeHeadImage(sk);
            using var data = head.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();

            if (_accountHeadCache is not null)
            {
                try
                {
                    await _accountHeadCache
                        .WriteAsync(AccountHeadCacheKey(trimmed), bytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Cache write is best-effort: a read-only cache dir mustn't break the flyout.
                }
            }

            using var headStream = new MemoryStream(bytes);
            return new Bitmap(headStream);
        }
        catch
        {
            return null;
        }
    }

    private static string AccountHeadCacheKey(string trimmedUuid) =>
        $"account-heads/{trimmedUuid}.png";

    /// <summary>Silent-sign-in the picked account and mark it active. Surfaced via the header chip flyout.</summary>
    private async Task SwitchAccountAsync(AccountWithBitmap? selected)
    {
        var target = selected?.Account;
        if (target is null || _microsoftAuth is null) return;
        IsBusy = true;
        try
        {
            Append($"Switching to '{target.Username}' ...");
            if (target.IsOffline)
            {
                // Offline placeholder: just retire the online session, mark active, the offline launch
                // path will pick the username up from ActiveAccount.
                if (_accountStore is not null)
                    await _accountStore.SetActiveAsync(target.Id, CancellationToken.None).ConfigureAwait(false);
                CurrentSession = null;
                ActiveAccount = target;
                Username = target.Username;
                Append($"Switched to offline account '{target.Username}'.");
                return;
            }

            var auth = await _microsoftAuth.SignInSilentlyAsync(target.Id, CancellationToken.None).ConfigureAwait(false);
            CurrentSession = auth;
            ActiveAccount = target with { LastUsedAt = DateTimeOffset.UtcNow };
            Append($"Switched to '{auth.Username}'.");
            await RefreshAccountsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not switch account: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Run a fresh device-code sign-in and add the resulting account to the roster.</summary>
    private async Task AddAccountAsync()
    {
        if (_microsoftAuth is null) return;
        IsBusy = true;
        try
        {
            Append("Adding new Microsoft account (device-code) ...");
            var auth = await _microsoftAuth.SignInInteractiveAsync(CancellationToken.None).ConfigureAwait(false);
            CurrentSession = auth;
            Append($"Added '{auth.Username}'.");
            await RefreshAccountsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not add account: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Remove the picked account from MSAL's cache and our roster.</summary>
    private async Task RemoveAccountAsync(Account? target)
    {
        if (target is null || _microsoftAuth is null) return;
        IsBusy = true;
        try
        {
            Append($"Removing account '{target.Username}' ...");
            await _microsoftAuth.SignOutAsync(target.Id, CancellationToken.None).ConfigureAwait(false);
            if (ActiveAccount?.Id == target.Id)
            {
                CurrentSession = null;
                ActiveAccount = null;
            }
            await RefreshAccountsAsync(CancellationToken.None).ConfigureAwait(false);
            Append($"Removed '{target.Username}'.");
        }
        catch (Exception ex)
        {
            Append($"[error] Could not remove account: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Unified refresh: loads our saved instances AND scans <c>.minecraft/versions/</c>,
    /// then merges installed versions that aren't already represented by a saved instance
    /// as auto-imported entries. Result: a single <see cref="Instances"/> collection that
    /// backs both the Home page combobox and the Installations grid.
    /// </summary>
    private async Task RefreshInstancesAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_LoadingInstances);
            var saved = await _service.ListInstancesAsync(CancellationToken.None);
            var installed = await _service.ListInstalledVersionsAsync(CancellationToken.None);

            // Keep InstalledVersions current too - the SelectedProfile flow still uses it
            // to surface the matching install when a launcher_profiles.json profile is picked.
            InstalledVersions.Clear();
            foreach (var v in installed) InstalledVersions.Add(v);

            Instances.Clear();
            var coveredVersionIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in saved)
            {
                Instances.Add(i);
                coveredVersionIds.Add(i.VersionId);
            }

            foreach (var v in installed)
            {
                if (coveredVersionIds.Contains(v.Id)) continue;
                Instances.Add(new Instance
                {
                    Id = $"installed:{v.Id}",
                    Name = v.Id,
                    VersionId = v.Id,
                    Loader = v.Loader,
                    IsAutoImported = true,
                    IconKey = v.Loader switch
                    {
                        TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.Forge => InstanceIcons.Cobblestone,
                        TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.NeoForge => InstanceIcons.Cobblestone,
                        TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.Fabric => InstanceIcons.Planks,
                        TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.Quilt => InstanceIcons.Planks,
                        _ => InstanceIcons.GrassBlock,
                    },
                });
            }

            var autoImported = Instances.Count - saved.Count;
            Append(autoImported > 0
                ? $"Loaded {saved.Count} instances (+{autoImported} auto-imported from .minecraft/versions/)."
                : $"Loaded {saved.Count} instances.");
            _logger.Info($"Instances refreshed ({saved.Count} saved, {autoImported} auto-imported).");
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedInstanceAsync()
    {
        if (SelectedInstance is not { } victim) return;
        if (victim.IsAutoImported)
        {
            // Auto-imported instances aren't in our store; the version folder belongs to
            // the official launcher and we leave it alone. The Delete button's CanExecute
            // already blocks this path, but the guard keeps the method honest.
            Append(Strings.Error_CannotDeleteAutoImported);
            return;
        }
        IsBusy = true;
        try
        {
            Append($"Deleting instance '{victim.Name}' ...");
            await _service.DeleteInstanceAsync(victim.Id, CancellationToken.None);
            Instances.Remove(victim);
            SelectedInstance = null;
        }
        catch (Exception ex)
        {
            Append($"[error] Could not delete instance: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Add a freshly-built instance to the store and the in-memory list. Returns the saved record.</summary>
    /// <param name="loaderVersion">Loader-specific version string. Stored on the new
    /// <see cref="Instance.LoaderVersion"/>. Ignored when <paramref name="loader"/> is
    /// <see cref="ModLoader.None"/>. Picked from the New Instance dialog's loader-version
    /// ComboBox in v0.30.0 (T21a).</param>
    public async Task<Instance> CreateInstanceAsync(string name, string versionId, string iconKey, ModLoader loader = ModLoader.None, string? loaderVersion = null)
    {
        var instance = new Instance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            VersionId = versionId,
            IconKey = iconKey,
            Loader = loader,
            LoaderVersion = loader == ModLoader.None ? null : loaderVersion,
        };
        await _service.SaveInstanceAsync(instance, CancellationToken.None);
        Instances.Insert(0, instance);
        var loaderSuffix = loader == ModLoader.None
            ? string.Empty
            : (string.IsNullOrEmpty(loaderVersion) ? $" ({loader})" : $" ({loader} {loaderVersion})");
        Append($"Created instance '{name}' for version {versionId}{loaderSuffix}.");
        return instance;
    }

    /// <summary>True once <see cref="ImportModpackAsync"/> has been called and not yet finished. Drives the modal progress UI.</summary>
    public bool IsImportingModpack
    {
        get => _isImportingModpack;
        private set => SetField(ref _isImportingModpack, value);
    }

    /// <summary>0.0..1.0 progress of an in-flight modpack import. Snapped to 1.0 on completion.</summary>
    public double ModpackImportProgress
    {
        get => _modpackImportProgress;
        private set => SetField(ref _modpackImportProgress, value);
    }

    /// <summary>Last status line emitted by an in-flight modpack import (filename, loader, error message).</summary>
    public string ModpackImportStatus
    {
        get => _modpackImportStatus;
        private set => SetField(ref _modpackImportStatus, value ?? string.Empty);
    }

    /// <summary>True when a modpack importer is wired in (App-layer DI). Hides the button when not available.</summary>
    public bool CanImportModpack => _modpackImporter is not null;

    /// <summary>
    /// Drive a single modpack import end-to-end: dispatch by format detection, surface progress
    /// through the bound properties, persist the freshly-built <see cref="Instance"/> via the
    /// launcher service, and insert it at the top of <see cref="Instances"/>. Returns the new
    /// instance on success or null on cancellation / failure (with a log entry either way).
    /// </summary>
    public async Task<Instance?> ImportModpackAsync(string archivePath, string? targetInstanceName, CancellationToken cancellationToken = default)
    {
        if (_modpackImporter is null)
        {
            Append(Strings.Error_ModpackImporterDisabled);
            return null;
        }
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            Append($"[error] Modpack archive not found: {archivePath}");
            return null;
        }

        IsImportingModpack = true;
        ModpackImportProgress = 0;
        ModpackImportStatus = $"Importing {Path.GetFileName(archivePath)} ...";
        IsBusy = true;
        try
        {
            var progress = new Progress<double>(p =>
            {
                // Snap negative / overshoot values back into range so the bar binding stays sane.
                ModpackImportProgress = p < 0 ? 0 : (p > 1 ? 1 : p);
            });
            var instance = await _modpackImporter.ImportAsync(archivePath, targetInstanceName, progress, cancellationToken).ConfigureAwait(true);
            await _service.SaveInstanceAsync(instance, cancellationToken).ConfigureAwait(true);
            Instances.Insert(0, instance);
            SelectedInstance = instance;
            ModpackImportProgress = 1.0;
            ModpackImportStatus = $"Imported '{instance.Name}' (version {instance.VersionId}{(instance.Loader == ModLoader.None ? string.Empty : $", {instance.Loader}")}).";
            Append(ModpackImportStatus);
            _logger.Info($"Modpack import complete: id={instance.Id}, version={instance.VersionId}, loader={instance.Loader}, gameDir={instance.GameDirectory}");
            return instance;
        }
        catch (OperationCanceledException)
        {
            Append(Strings.Error_ModpackImportCancelled);
            return null;
        }
        catch (Exception ex)
        {
            ModpackImportStatus = $"Import failed: {ex.Message}";
            Append($"[error] Modpack import failed: {ex.Message}");
            _logger.Error("Modpack import failed.", ex);
            return null;
        }
        finally
        {
            IsImportingModpack = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Query the available loader versions for <paramref name="loader"/> on
    /// <paramref name="minecraftVersion"/>. Used by the New Instance dialog to fill the
    /// loader-version ComboBox after the user picks a non-Vanilla loader chip.
    /// Returns an empty list when the version fetcher is not configured (headless / test
    /// contexts) or the underlying probe failed.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListLoaderVersionsAsync(ModLoader loader, string minecraftVersion, CancellationToken cancellationToken)
    {
        if (_modLoaderVersionFetcher is null) return Array.Empty<string>();
        if (loader == ModLoader.None || string.IsNullOrWhiteSpace(minecraftVersion)) return Array.Empty<string>();
        try
        {
            return await _modLoaderVersionFetcher.ListLoaderVersionsAsync(loader, minecraftVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network / API hiccups shouldn't crash the dialog - show the chip without a
            // version list and let the user pick "latest" instead.
            _logger.Warn($"Could not list {loader} versions for {minecraftVersion}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Persist a new icon for an existing user-created instance and swap the in-memory
    /// record in the <see cref="Instances"/> collection so the tile re-binds. Auto-imported
    /// instances are refused (they live under the official launcher's <c>.minecraft/versions/</c>
    /// folder and Hyperion doesn't persist them). Returns the updated record on success, null
    /// when the call is rejected (auto-imported / no-op).
    /// </summary>
    public async Task<Instance?> ChangeInstanceIconAsync(Instance instance, string newIconKey)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(newIconKey);

        if (instance.IsAutoImported)
        {
            // Caller (the View) gates the menu item, but the VM keeps the same guard so the
            // contract is enforced regardless of which surface invokes it.
            Append(Strings.Error_CannotChangeAutoImportedIcon);
            return null;
        }

        if (string.Equals(instance.IconKey, newIconKey, StringComparison.Ordinal))
            return instance; // no-op: don't churn the file, don't log noise.

        var updated = instance with { IconKey = newIconKey };
        await _service.SaveInstanceAsync(updated, CancellationToken.None);

        var idx = Instances.IndexOf(instance);
        if (idx >= 0)
        {
            Instances[idx] = updated;
            if (ReferenceEquals(SelectedInstance, instance))
                SelectedInstance = updated;
        }

        _logger.Info($"Instance {updated.Id} ({updated.Name}) icon changed to {newIconKey}.");
        return updated;
    }

    /// <summary>
    /// Inject the View's "pick where to save the export zip" delegate. The View calls this
    /// from its <c>Window.Opened</c> handler once the <c>TopLevel</c> is available.
    /// </summary>
    public void SetExportZipPickRequest(InstanceExportZipPickRequest? request)
    {
        _exportZipPickRequest = request;
    }

    /// <summary>Inject the View's "pick the zip to import" delegate.</summary>
    public void SetImportZipPickRequest(InstanceImportZipPickRequest? request)
    {
        _importZipPickRequest = request;
    }

    /// <summary>
    /// Export <paramref name="instance"/> to a user-picked <c>.zip</c> path. Wired by the
    /// per-tile "Export to zip..." menu item. Public so tests can drive the flow directly.
    /// </summary>
    public async Task ExportInstanceAsync(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_instanceExporter is null)
        {
            Append(Strings.Error_InstanceExportUnavailable);
            return;
        }
        if (instance.IsAutoImported)
        {
            // Auto-imported instances live under .minecraft/versions/ and we don't own
            // their JSON record - share isn't meaningful here.
            Append(Strings.Error_CannotExportAutoImported);
            return;
        }
        if (_exportZipPickRequest is null)
        {
            Append(Strings.Error_ExportPickerUnavailable);
            return;
        }

        var suggested = SanitiseFileName(instance.Name) + ".zip";
        string? destination;
        try
        {
            destination = await _exportZipPickRequest(suggested, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not show export file picker: {ex.Message}");
            return;
        }
        if (string.IsNullOrWhiteSpace(destination))
        {
            Append(Strings.Error_InstanceExportCancelled);
            return;
        }

        IsBusy = true;
        try
        {
            Append($"Exporting instance '{instance.Name}' to {destination} ...");
            var progress = new Progress<double>(v =>
            {
                // Only log progress at 10% increments so we don't flood the log with every byte tick.
                var pct = (int)(v * 100);
                if (pct == 0 || pct == 100 || pct % 25 == 0) Append($"Export progress: {pct}%");
            });

            await _instanceExporter.ExportAsync(instance, destination, progress, CancellationToken.None).ConfigureAwait(true);
            Append($"Exported instance '{instance.Name}' to {destination}.");
            _logger.Info($"Instance {instance.Id} ({instance.Name}) exported to {destination}.");
        }
        catch (Exception ex)
        {
            Append($"[error] Could not export instance: {ex.Message}");
            _logger.Warn($"Export of instance {instance.Id} failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ExportSelectedInstanceAsync()
        => SelectedInstance is { } inst ? ExportInstanceAsync(inst) : Task.CompletedTask;

    /// <summary>
    /// Import an instance from a user-picked Hyperion-format <c>.zip</c>. Wired by the
    /// "Import from zip..." top-level button. Re-runs <see cref="RefreshInstancesAsync"/>
    /// on success so the tile grid picks the new entry up.
    /// </summary>
    public async Task ImportInstanceAsync()
    {
        if (_instanceImporter is null)
        {
            Append(Strings.Error_InstanceImportUnavailable);
            return;
        }
        if (_importZipPickRequest is null)
        {
            Append(Strings.Error_ImportPickerUnavailable);
            return;
        }

        string? source;
        try
        {
            source = await _importZipPickRequest(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not show import file picker: {ex.Message}");
            return;
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            Append(Strings.Error_InstanceImportCancelled);
            return;
        }

        IsBusy = true;
        try
        {
            Append($"Importing instance from {source} ...");
            var progress = new Progress<double>(v =>
            {
                var pct = (int)(v * 100);
                if (pct == 0 || pct == 100 || pct % 25 == 0) Append($"Import progress: {pct}%");
            });

            var imported = await _instanceImporter.ImportAsync(source, overrideName: null, progress, CancellationToken.None).ConfigureAwait(true);
            await _service.SaveInstanceAsync(imported, CancellationToken.None).ConfigureAwait(true);
            await RefreshInstancesAsync().ConfigureAwait(true);

            // Pick the freshly-imported tile so the user can see the result lit up.
            var match = Instances.FirstOrDefault(i => i.Id == imported.Id);
            if (match is not null) SelectedInstance = match;

            Append($"Imported instance '{imported.Name}' (id={imported.Id}).");
            _logger.Info($"Instance imported from {source}: {imported.Id} / {imported.Name}.");
        }
        catch (InstanceImportException ex)
        {
            // Friendly message - this is the path the spec asks us to surface to the user.
            Append($"[error] Could not import instance: {ex.Message}");
            _logger.Warn($"Instance import rejected: {ex.Message}");
        }
        catch (Exception ex)
        {
            Append($"[error] Could not import instance: {ex.Message}");
            _logger.Warn($"Instance import failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SanitiseFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "instance";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
            safe.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return safe.ToString();
    }

    /// <summary>
    /// Persist a fully-edited instance record (name, icon, per-instance overrides) from the
    /// Edit Instance dialog. Refuses auto-imported instances so the dialog never accidentally
    /// writes a sibling JSON beside the official launcher's <c>versions/</c> folder.
    /// Returns the persisted record on success, <c>null</c> when refused.
    /// </summary>
    public async Task<Instance?> ApplyEditedInstanceAsync(Instance original, Instance edited)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edited);

        if (original.IsAutoImported)
        {
            Append(Strings.Error_CannotEditAutoImported);
            return null;
        }

        // Keep the immutable identity fields no matter what the dialog returned.
        var updated = edited with
        {
            Id = original.Id,
            CreatedAt = original.CreatedAt,
            IsAutoImported = false,
            LastPlayedAt = original.LastPlayedAt,
        };

        await _service.SaveInstanceAsync(updated, CancellationToken.None);

        var idx = Instances.IndexOf(original);
        if (idx >= 0)
        {
            Instances[idx] = updated;
            if (ReferenceEquals(SelectedInstance, original))
                SelectedInstance = updated;
        }

        _logger.Info($"Instance {updated.Id} ({updated.Name}) edited.");
        Append($"Saved changes to '{updated.Name}'.");
        return updated;
    }

    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null) return;
        IsBusy = true;
        try
        {
            Append(Strings.Log_SavingSettings);
            await _settingsStore.SaveAsync(BuildSettings(), CancellationToken.None);
            Append(string.Format(Strings.Log_SettingsSaved, MinMemoryMb, MaxMemoryMb));
        }
        catch (Exception ex)
        {
            Append(string.Format(Strings.Log_CouldNotSaveSettings, ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshNewsAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_FetchingNews);
            var news = await _service.ListNewsAsync(CancellationToken.None);

            // T18: Mojang's feed is already newest-first, so we DON'T re-sort here. If you add an
            // OrderByDescending(n => n.PublishedAt) "to be safe", you'll add an LINQ enumeration
            // on every refresh for zero behavioural change. Trust the upstream order.
            News.Clear();
            foreach (var n in news)
                News.Add(n);

            Append(string.Format(Strings.Log_LoadedNNewsArticles, news.Count));
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshServersAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_ReadingServersDat);
            var servers = await _service.ListServersAsync(CancellationToken.None);

            Servers.Clear();
            foreach (var s in servers)
                Servers.Add(new ServerListItemViewModel(s));

            Append(string.Format(Strings.Log_LoadedNServers, servers.Count));
            RefreshServerPingsCommand.RaiseCanExecuteChanged();
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fire one ping per server (3-second timeout each) and update the per-row VM in place.
    /// Marked busy for the whole batch so the user can't fire it twice; pings run in parallel
    /// under <see cref="IMinecraftLauncherService.PingServersAsync"/>.
    /// </summary>
    private async Task RefreshServerPingsAsync()
    {
        if (Servers.Count == 0) return;
        IsBusy = true;
        try
        {
            Append($"Pinging {Servers.Count} servers ...");
            foreach (var row in Servers) row.IsPinging = true;

            var entries = new System.Collections.Generic.List<ServerListEntry>(Servers.Count);
            foreach (var row in Servers) entries.Add(row.Entry);

            var results = await _service.PingServersAsync(entries, CancellationToken.None);

            int reachable = 0;
            foreach (var row in Servers)
            {
                row.IsPinging = false;
                results.TryGetValue(row.Entry.Ip ?? string.Empty, out var status);
                row.ApplyStatus(status);
                if (status is not null) reachable++;
            }
            Append($"Pinged {Servers.Count} servers ({reachable} reachable).");
        }
        catch (LauncherException ex)
        {
            foreach (var row in Servers) row.IsPinging = false;
            Append($"[error] {ex.Message}");
        }
        catch (Exception ex)
        {
            foreach (var row in Servers) row.IsPinging = false;
            Append($"[error] Ping failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshProfilesAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_ReadingLauncherProfiles);
            var profiles = await _service.ListProfilesAsync(CancellationToken.None);

            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(p);

            Append(string.Format(Strings.Log_LoadedNProfiles, profiles.Count));
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshInstalledVersionsAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_ScanningInstalledVersions);
            var versions = await _service.ListInstalledVersionsAsync(CancellationToken.None);

            InstalledVersions.Clear();
            foreach (var v in versions)
                InstalledVersions.Add(v);

            Append(string.Format(Strings.Log_FoundNInstalledVersions, versions.Count));
            _logger.Info($"Installed versions scanned ({versions.Count} entries).");
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
            _logger.Warn($"RefreshInstalledVersions surfaced LauncherException to UI: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-raised from <see cref="IMicrosoftAuthService.DeviceCodeRequested"/> on the UI thread,
    /// so the View can show a modal dialog with the readable code.
    /// </summary>
    public event EventHandler<MicrosoftDeviceCodeInfo>? DeviceCodeRequested;

    private void OnDeviceCodeRequested(object? sender, MicrosoftDeviceCodeInfo info)
    {
        // The MSAL device-code callback runs off the UI thread. Marshal back so Append
        // (which raises PropertyChanged on LogText) updates bindings cleanly AND so the
        // re-raised DeviceCodeRequested fires on the dispatcher thread (View opens a modal).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Append(string.Format(Strings.Log_MicrosoftSignInPrompt, info.UserCode, info.VerificationUrl));
            DeviceCodeRequested?.Invoke(this, info);
        });
    }

    private async Task SignInMicrosoftAsync()
    {
        IsBusy = true;
        try
        {
            Append(Strings.Log_SigningInWithMicrosoft);
            var auth = await _service.AuthenticateAsync(
                new AuthRequest { Mode = AuthMode.Microsoft, Username = string.Empty },
                CancellationToken.None);

            CurrentSession = auth;
            Append(string.Format(Strings.Log_SignedInAs, auth.Username));
            _logger.Info($"UI: Microsoft sign-in succeeded for '{auth.Username}'.");

            // Populate OwnedSkins / OwnedCapes from the Mojang profile so the Skins page
            // is ready to use without an extra click.
            if (!auth.IsOffline)
                await TryRefreshProfileAsync(auth.AccessToken);
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
            _logger.Warn($"Microsoft sign-in surfaced LauncherException to UI: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            if (_microsoftAuth is not null)
            {
                await _microsoftAuth.SignOutAsync(CancellationToken.None);
            }
            CurrentSession = null;
            // Drop the now-stale profile data; the Skins page will show empty pickers.
            OwnedSkins = Array.Empty<OwnedSkin>();
            OwnedCapes = Array.Empty<OwnedCape>();
            _suppressCapeSelectionWrite = true;
            try { SelectedActiveCape = null; }
            finally { _suppressCapeSelectionWrite = false; }
            Append(Strings.Log_SignedOut);
            _logger.Info("UI: signed out.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Skins ----

    /// <summary>The list of skins on the player's Mojang profile. Populated on sign-in.</summary>
    public IReadOnlyList<OwnedSkin> OwnedSkins
    {
        get => _ownedSkins;
        private set
        {
            if (SetField(ref _ownedSkins, value ?? Array.Empty<OwnedSkin>()))
                OnPropertyChanged(nameof(HasOwnedCapes));
        }
    }

    /// <summary>The capes the player owns. <see cref="HasOwnedCapes"/> drives the picker visibility.</summary>
    public IReadOnlyList<OwnedCape> OwnedCapes
    {
        get => _ownedCapes;
        private set
        {
            if (SetField(ref _ownedCapes, value ?? Array.Empty<OwnedCape>()))
                OnPropertyChanged(nameof(HasOwnedCapes));
        }
    }

    /// <summary>True when the player owns at least one cape (controls cape-picker visibility).</summary>
    public bool HasOwnedCapes => _ownedCapes.Count > 0;

    /// <summary>
    /// Bound to the cape picker on the Skins page. Setting this to a cape triggers
    /// <see cref="SetActiveCapeCommand"/>; setting it to <c>null</c> triggers
    /// <see cref="ClearActiveCapeCommand"/>. Programmatic updates (the View-Model itself
    /// re-syncs after a server roundtrip) suppress that auto-fire to avoid loops.
    /// </summary>
    public OwnedCape? SelectedActiveCape
    {
        get => _selectedActiveCape;
        set
        {
            if (!SetField(ref _selectedActiveCape, value)) return;
            if (_suppressCapeSelectionWrite) return;
            // Fire-and-forget; errors surface in the log via the underlying methods.
            if (value is null)
                _ = ClearActiveCapeAsync();
            else
                _ = SetActiveCapeAsync(value.Id);
        }
    }

    /// <summary>Most-recent-first list of skin history entries (capped at <see cref="FileSkinHistoryStore.Capacity"/>).</summary>
    public ObservableCollection<SkinHistoryEntry> SkinHistory { get; }

    /// <summary>
    /// Latest community gallery surfaced by the configured <see cref="ISkinBrowser"/>
    /// (NameMC by default). Populated by <see cref="RefreshTrendingSkinsCommand"/> and
    /// <see cref="SearchSkinsCommand"/>; empty when no browser is wired or the user
    /// hasn't opened the Skins page yet.
    /// </summary>
    public ObservableCollection<BrowsedSkin> BrowsedSkins { get; }

    /// <summary>User-typed query for the skin gallery search box. Empty = "trending only".</summary>
    public string SkinSearchText
    {
        get => _skinSearchText;
        set => SetField(ref _skinSearchText, value ?? string.Empty);
    }

    /// <summary>Currently-previewed gallery card. Drives the right-hand preview pane.</summary>
    public BrowsedSkin? SelectedBrowsedSkin
    {
        get => _selectedBrowsedSkin;
        set
        {
            if (SetField(ref _selectedBrowsedSkin, value))
            {
                OnPropertyChanged(nameof(HasSelectedBrowsedSkin));
                ApplyBrowsedSkinCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when a card is selected and the apply panel should be visible.</summary>
    public bool HasSelectedBrowsedSkin => _selectedBrowsedSkin is not null;

    /// <summary>True when the gallery is currently being refreshed / searched.</summary>
    public bool IsSkinBrowserBusy
    {
        get => _skinBrowserBusy;
        private set
        {
            if (SetField(ref _skinBrowserBusy, value))
            {
                RefreshTrendingSkinsCommand.RaiseCanExecuteChanged();
                SearchSkinsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when an <see cref="ISkinBrowser"/> is wired (controls visibility of the gallery section).</summary>
    public bool IsSkinBrowserAvailable => _skinBrowser is not null;

    /// <summary>
    /// Inject the View's file-picker + variant-prompt delegate. The View calls this from
    /// its <c>Window.Opened</c> handler once the <c>TopLevel</c> is available; the
    /// view-model owns no Avalonia dependency itself.
    /// </summary>
    public void SetSkinPickRequest(SkinPickRequest? request)
    {
        _skinPickRequest = request;
    }

    /// <summary>
    /// Inject the View's CurseForge onboarding-dialog delegate. The View calls this from its
    /// <c>Window.Opened</c> handler so the VM can request "show the onboarding dialog and
    /// give me back the entered key" without taking a direct Avalonia dependency.
    /// </summary>
    public void SetCurseForgeKeyRequest(CurseForgeKeyRequest? request)
    {
        _curseForgeKeyRequest = request;
        OpenCurseForgeOnboardingCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Show the CurseForge onboarding modal and, if the user pasted a key and clicked Save,
    /// persist it through the settings store and push it into the live repository slot so
    /// the next search hits CurseForge without the user having to restart the launcher.
    /// </summary>
    private async Task OpenCurseForgeOnboardingAsync()
    {
        if (_settingsStore is null || _curseForgeKeyRequest is null) return;
        IsBusy = true;
        try
        {
            string? newKey;
            try
            {
                newKey = await _curseForgeKeyRequest(_curseForgeApiKey ?? string.Empty, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Append($"[error] Could not open CurseForge onboarding: {ex.Message}");
                return;
            }
            if (newKey is null)
            {
                // User cancelled - leave existing key untouched, no log spam.
                return;
            }

            try
            {
                // Load-modify-save so any other fields the user has touched in this session
                // but not yet committed via Save Settings are preserved.
                var current = await _settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                var patched = current with { CurseForgeApiKey = newKey };
                await _settingsStore.SaveAsync(patched, CancellationToken.None).ConfigureAwait(true);
                _curseForgeApiKey = newKey;
                // Push the new key into the live repo slot so the next SearchAsync resolves
                // the fresh value (otherwise the user would have to restart the launcher).
                _curseForgeKeySetter?.Invoke(newKey);
                OnPropertyChanged(nameof(HasCurseForgeKey));
                OnPropertyChanged(nameof(ShouldShowCurseForgeEmptyState));
                OnPropertyChanged(nameof(CurseForgeApiKey));
                Append("[mods] CurseForge API key saved. Try searching now.");
            }
            catch (Exception ex)
            {
                Append($"[error] Could not persist CurseForge key: {ex.Message}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UploadSkinAsync()
    {
        if (_skinService is null || _currentSession is not { IsOffline: false } online)
        {
            Append(Strings.Error_NoSession);
            return;
        }
        if (_skinPickRequest is null)
        {
            Append(Strings.Error_SkinPickerUnavailable);
            return;
        }

        IsBusy = true;
        try
        {
            SkinPickResult? picked;
            try
            {
                picked = await _skinPickRequest(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Append($"[error] Could not pick skin: {ex.Message}");
                return;
            }
            if (picked is null)
            {
                Append(Strings.Error_SkinUploadCancelled);
                return;
            }

            await UploadAndArchiveAsync(online.AccessToken, picked.PngBytes, picked.Variant).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UploadAndArchiveAsync(string accessToken, byte[] pngBytes, SkinVariant variant)
    {
        if (_skinService is null) return;
        try
        {
            Append($"Uploading skin ({variant}) ...");
            await _skinService.UploadSkinAsync(accessToken, pngBytes, variant, CancellationToken.None).ConfigureAwait(false);
            Append($"Skin upload succeeded ({variant}).");
            _logger.Info($"UI: skin upload succeeded ({variant}).");

            if (_skinHistory is not null)
            {
                try
                {
                    await _skinHistory.AppendAsync(pngBytes, variant, CancellationToken.None).ConfigureAwait(false);
                    await ReloadHistoryAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Could not append skin to history: {ex.Message}");
                }
            }

            await TryRefreshProfileAsync(accessToken).ConfigureAwait(false);
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
            _logger.Warn($"Skin upload surfaced LauncherException to UI: {ex.Message}");
        }
        catch (Exception ex)
        {
            Append($"[error] Skin upload failed: {ex.Message}");
        }
    }

    private async Task SetActiveCapeFromSelectionAsync()
    {
        var id = _selectedActiveCape?.Id;
        if (string.IsNullOrEmpty(id))
        {
            await ClearActiveCapeAsync().ConfigureAwait(false);
            return;
        }
        await SetActiveCapeAsync(id).ConfigureAwait(false);
    }

    private async Task SetActiveCapeAsync(string capeId)
    {
        if (_skinService is null || _currentSession is not { IsOffline: false } online)
        {
            Append(Strings.Error_CapeOpsRequireMsAccount);
            return;
        }

        IsBusy = true;
        try
        {
            Append($"Activating cape '{capeId}' ...");
            await _skinService.SetActiveCapeAsync(online.AccessToken, capeId, CancellationToken.None).ConfigureAwait(false);
            Append("Cape activated.");
            await TryRefreshProfileAsync(online.AccessToken).ConfigureAwait(false);
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearActiveCapeAsync()
    {
        if (_skinService is null || _currentSession is not { IsOffline: false } online)
        {
            Append(Strings.Error_CapeOpsRequireMsAccount);
            return;
        }

        IsBusy = true;
        try
        {
            Append("Clearing active cape ...");
            await _skinService.ClearActiveCapeAsync(online.AccessToken, CancellationToken.None).ConfigureAwait(false);
            Append("Cape cleared.");
            await TryRefreshProfileAsync(online.AccessToken).ConfigureAwait(false);
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReapplyHistoricSkinAsync(SkinHistoryEntry? entry)
    {
        if (entry is null) return;
        if (_skinService is null || _currentSession is not { IsOffline: false } online)
        {
            Append(Strings.Error_NoSession);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(entry.FilePath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not read history skin: {ex.Message}");
            return;
        }

        IsBusy = true;
        try
        {
            await UploadAndArchiveAsync(online.AccessToken, bytes, entry.Variant).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- NameMC skin browser (T-namemc, v0.32.1) ----

    /// <summary>Default cap on the number of browsed-skin cards loaded into the gallery.</summary>
    /// <remarks>
    /// NameMC's trending grid is paginated; we surface the first page only. 60 cards is the
    /// rough fit for the WrapPanel on a 1080p main window without scroll fatigue.
    /// </remarks>
    public const int BrowsedSkinLimit = 60;

    /// <summary>
    /// Pull the trending gallery from the configured <see cref="ISkinBrowser"/> and replace
    /// <see cref="BrowsedSkins"/>. Never throws: failures surface through the launcher log.
    /// </summary>
    public async Task RefreshTrendingBrowsedSkinsAsync()
    {
        if (_skinBrowser is null) return;
        if (_skinBrowserBusy) return;

        IsSkinBrowserBusy = true;
        try
        {
            var skins = await _skinBrowser.ListTrendingAsync(BrowsedSkinLimit, CancellationToken.None).ConfigureAwait(false);
            await ReplaceBrowsedSkinsAsync(skins).ConfigureAwait(false);
            _logger.Info($"NameMC: loaded {skins.Count} trending skins.");
        }
        catch (Exception ex)
        {
            Append(string.Format(Strings.SkinsBrowser_FetchFailed, ex.Message));
            _logger.Warn($"NameMC trending fetch failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            IsSkinBrowserBusy = false;
        }
    }

    /// <summary>
    /// Run <see cref="SkinSearchText"/> against the configured <see cref="ISkinBrowser"/>.
    /// Empty / whitespace queries fall back to trending. Never throws.
    /// </summary>
    public async Task SearchBrowsedSkinsAsync()
    {
        if (_skinBrowser is null) return;
        if (_skinBrowserBusy) return;

        var query = _skinSearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            await RefreshTrendingBrowsedSkinsAsync().ConfigureAwait(false);
            return;
        }

        IsSkinBrowserBusy = true;
        try
        {
            var skins = await _skinBrowser.SearchAsync(query, BrowsedSkinLimit, CancellationToken.None).ConfigureAwait(false);
            await ReplaceBrowsedSkinsAsync(skins).ConfigureAwait(false);
            _logger.Info($"NameMC: search '{query}' returned {skins.Count} skins.");
        }
        catch (Exception ex)
        {
            Append(string.Format(Strings.SkinsBrowser_FetchFailed, ex.Message));
            _logger.Warn($"NameMC search '{query}' failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            IsSkinBrowserBusy = false;
        }
    }

    /// <summary>
    /// Download <paramref name="skin"/>'s PNG and push it through the existing
    /// upload + history append pipeline. Requires a signed-in Microsoft session.
    /// </summary>
    private async Task ApplyBrowsedSkinAsync(BrowsedSkin? skin)
    {
        if (skin is null) return;
        if (_skinBrowser is null || _skinService is null) return;
        if (_currentSession is not { IsOffline: false } online)
        {
            Append(Strings.Error_NoSession);
            return;
        }

        IsBusy = true;
        try
        {
            Append(string.Format(Strings.SkinsBrowser_Applying, skin.Id));
            byte[] bytes;
            try
            {
                bytes = await _skinBrowser.DownloadPngAsync(skin, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Append(string.Format(Strings.SkinsBrowser_DownloadFailed, ex.Message));
                _logger.Warn($"NameMC download failed for {skin.Id}: {ex.Message}");
                return;
            }

            await UploadAndArchiveAsync(online.AccessToken, bytes, skin.Variant).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReplaceBrowsedSkinsAsync(IReadOnlyList<BrowsedSkin> skins)
    {
        void Apply()
        {
            BrowsedSkins.Clear();
            foreach (var s in skins) BrowsedSkins.Add(s);
            // Clear any stale selection that no longer points at a visible card.
            if (_selectedBrowsedSkin is not null && skins.All(s => s.Id != _selectedBrowsedSkin.Id))
                SelectedBrowsedSkin = null;
        }

        // Mirror the marshaling pattern from ReloadHistoryAsync so tests (which run without
        // an Avalonia dispatcher) execute the update inline rather than blocking on a Post.
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            Apply();
        else
            await dispatcher.InvokeAsync(Apply);
    }

    /// <summary>
    /// Fetch the player profile (skins + capes) and push it into the OwnedSkins / OwnedCapes
    /// collections + SelectedActiveCape. Surfaces failures through the log; never throws.
    /// </summary>
    public async Task TryRefreshProfileAsync(string accessToken)
    {
        if (_skinService is null) return;
        if (string.IsNullOrWhiteSpace(accessToken)) return;
        try
        {
            var profile = await _skinService.GetProfileAsync(accessToken, CancellationToken.None).ConfigureAwait(false);
            OwnedSkins = profile.Skins;
            OwnedCapes = profile.Capes;

            // Suppress the setter's auto-fire while we re-sync from the server snapshot.
            _suppressCapeSelectionWrite = true;
            try
            {
                SelectedActiveCape = profile.Capes.FirstOrDefault(c =>
                    string.Equals(c.State, "ACTIVE", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressCapeSelectionWrite = false;
            }
        }
        catch (LauncherException ex)
        {
            Append($"[error] Could not load profile: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"GetProfile threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Pull the on-disk skin-history index into the <see cref="SkinHistory"/> collection.</summary>
    public async Task ReloadHistoryAsync()
    {
        if (_skinHistory is null) return;
        try
        {
            var entries = await _skinHistory.ListAsync(CancellationToken.None).ConfigureAwait(false);

            void Apply()
            {
                SkinHistory.Clear();
                foreach (var e in entries) SkinHistory.Add(e);
            }

            // In production we always have an Avalonia dispatcher; in xunit we don't, in
            // which case the platform field is null and Dispatcher.UIThread.Post would hang.
            // Probe via reflection (CheckAccess on a null platform returns true).
            var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
            if (dispatcher.CheckAccess())
                Apply();
            else
                await dispatcher.InvokeAsync(Apply);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not load skin history: {ex.Message}");
        }
    }

    private async Task LaunchAsync()
    {
        // Priority: explicit Instance > already-installed version > manifest version.
        var versionName = SelectedInstance?.VersionId ?? SelectedInstalledVersion?.Id ?? SelectedVersion?.Name;
        if (string.IsNullOrEmpty(versionName))
            return;

        await LaunchCoreAsync(SelectedInstance, versionName, new QuickPlay.None());
    }

    /// <summary>
    /// Quick Play launch entry: deep-link straight into a world or server. Used by the Servers
    /// "Join" button and the Worlds "Resume" button. Overrides the user's normal launch selection -
    /// resolves the version from the provided instance.
    /// </summary>
    /// <param name="instance">The instance whose version should be launched. Required.</param>
    /// <param name="target">Quick Play target. <see cref="QuickPlay.None"/> is allowed but will
    /// produce a regular launch (the caller should use the plain Launch button in that case).</param>
    public async Task QuickPlayLaunchAsync(Instance instance, QuickPlay target)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(target);

        var targetLabel = target switch
        {
            QuickPlay.Multiplayer mp => $"{mp.Host}:{mp.Port}",
            QuickPlay.Singleplayer sp => sp.WorldFolderName,
            _ => "main menu",
        };
        Append($"Quick play: joining {targetLabel}.");
        await LaunchCoreAsync(instance, instance.VersionId, target);
    }

    private async Task LaunchCoreAsync(Instance? instance, string versionName, QuickPlay quickPlay)
    {
        IsBusy = true;
        try
        {
            AuthResult auth;
            if (CurrentSession is { IsOffline: false } online)
            {
                auth = online;
                Append($"Launching as '{auth.Username}' (Microsoft session).");
            }
            else
            {
                Append($"Authenticating '{Username}' (offline mode) ...");
                auth = await _service.AuthenticateAsync(
                    new AuthRequest { Mode = AuthMode.Offline, Username = Username },
                    CancellationToken.None);
            }

            // Synchronous progress wrapper - see project CLAUDE.md "Threading" for the rationale.
            var progress = new SynchronousProgress<LaunchProgress>(p =>
            {
                var fractionText = p.Fraction is double frac
                    ? $" {(frac * 100).ToString("0", CultureInfo.InvariantCulture)}%"
                    : string.Empty;
                var itemText = string.IsNullOrEmpty(p.CurrentItem) ? string.Empty : $" - {p.CurrentItem}";
                Append($"{p.Stage}{fractionText}{itemText}");
            });

            // Manual Java override wins: when the user has pinned a specific java.exe in Settings,
            // pass it through verbatim and skip the auto-download. Otherwise let the service pull
            // the matching Adoptium Temurin runtime for the requested Minecraft version.
            var javaOverride = string.IsNullOrWhiteSpace(_javaExecutableOverride) ? null : _javaExecutableOverride;
            var javaRequirement = javaOverride is null ? JavaRequirementResolver.For(versionName) : (JavaRequirement?)null;

            // Pre-launch world backup (T21f). Courtesy, not a blocker - any failure is logged and
            // the launch proceeds. Only runs when the toggle is on, a backup service was injected,
            // and the instance actually has at least one non-empty world to back up.
            if (instance is { } toBackup && _autoBackupBeforeLaunch && _backupService is not null)
            {
                await BackupInstanceWorldsAsync(toBackup, CancellationToken.None).ConfigureAwait(true);
            }
            // T21a, v0.30.0: forward the instance's mod loader so CmlLibMinecraftLauncherService
            // can run IModLoaderInstaller before the regular install. Vanilla instances (or any
            // launch path with no instance, e.g. direct version pick) leave Loader == None and
            // the launcher service short-circuits the loader-install block entirely.
            var loader = instance?.Loader ?? ModLoader.None;
            var loaderVersion = instance?.LoaderVersion;

            Append($"Launching {versionName} (Xms={MinMemoryMb}M, Xmx={MaxMemoryMb}M) ...");
            // Per-instance overrides win over the global Settings page values; blank or zero
            // fields on the instance fall through to the Settings defaults (see Merge helper).
            var resolved = InstanceLaunchSettings.Merge(instance, BuildSettings());

            Append($"Launching {versionName} (Xms={resolved.MinimumRamMb}M, Xmx={resolved.MaximumRamMb}M) ...");
            var result = await _service.LaunchAsync(
                new LaunchRequest
                {
                    VersionName = versionName,
                    Session = auth,
                    GameDirectory = resolved.GameDirectory,
                    MinimumRamMb = resolved.MinimumRamMb,
                    MaximumRamMb = resolved.MaximumRamMb,
                    JvmArguments = string.IsNullOrWhiteSpace(resolved.JvmArguments) ? null : resolved.JvmArguments,
                    ScreenWidth = resolved.ResolutionWidth,
                    ScreenHeight = resolved.ResolutionHeight,
                    QuickPlay = quickPlay,
                    JavaRequirement = javaRequirement,
                    JavaPath = javaOverride,
                    Loader = loader,
                    LoaderVersion = loaderVersion,
                    // T-stdout-pipe (v0.32.1): ask the underlying launcher to redirect the child's
                    // stdout/stderr only when the user has the in-launcher log mirror toggle on.
                    // The launcher service then attaches an observable to LaunchResult.GameLogStream
                    // which we subscribe to below.
                    CaptureGameLog = _showGameLog,
                },
                progress,
                CancellationToken.None);

            Append($"Launched. pid={result.ProcessId} version={result.VersionName}");
            _logger.Info($"UI: launch complete (pid {result.ProcessId}, version {result.VersionName}).");

            // T-stdout-pipe (v0.32.1): subscribe to the live game log when capture is on. Each
            // line lands in the same LogText buffer the rest of the launcher writes to, prefixed
            // with [game] so users can tell it apart from launcher messages. The subscription
            // self-cancels when the Subject completes (the underlying launcher fires OnCompleted
            // from Process.Exited). We never await this - it's a fire-and-forget bridge that
            // lives for the lifetime of the game process.
            if (result.GameLogStream is { } gameLog)
            {
                _ = SubscribeToGameLogAsync(gameLog);
            }

            // Update Discord presence to "Playing <version> - <instance>" now that the
            // game process is up. Cosmetic only - any failure here is logged and swallowed.
            try { _presence.SetPlaying(result.VersionName, SelectedInstance?.Name); }
            catch (Exception ex) { _logger.Warn($"Presence SetPlaying failed: {ex.Message}"); }

            // Mark "last played" on the running instance so the grid sorts it to the front next time.
            // Auto-imported instances aren't in our store - just refresh their in-memory copy
            // so the UI reacts, without persisting.
            if (instance is { } inst)
            {
                var bumped = inst with { LastPlayedAt = DateTimeOffset.UtcNow };
                try
                {
                    if (!inst.IsAutoImported)
                        await _service.SaveInstanceAsync(bumped, CancellationToken.None);
                    var idx = Instances.IndexOf(inst);
                    if (idx >= 0)
                    {
                        Instances.RemoveAt(idx);
                        Instances.Insert(0, bumped);
                        if (ReferenceEquals(SelectedInstance, inst))
                            SelectedInstance = bumped;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Could not stamp LastPlayedAt on instance: {ex.Message}");
                }
            }
        }
        catch (LauncherException ex)
        {
            Append($"[error] {ex.Message}");
            _logger.Warn($"Launch surfaced LauncherException to UI: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Per-instance browser (screenshots / worlds / servers) ----

    private async Task RefreshInstanceScreenshotsAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            InstanceScreenshots = await _instanceBrowser.ListScreenshotsAsync(inst, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list screenshots: {ex.Message}");
            InstanceScreenshots = Array.Empty<ScreenshotEntry>();
        }
    }

    private async Task RefreshInstanceWorldsAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            InstanceWorlds = await _instanceBrowser.ListWorldsAsync(inst, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list worlds: {ex.Message}");
            InstanceWorlds = Array.Empty<WorldEntry>();
        }
    }

    private async Task RefreshInstanceServersAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            InstanceServers = await _instanceBrowser.ListServersAsync(inst, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list instance servers: {ex.Message}");
            InstanceServers = Array.Empty<ServerListEntry>();
        }
    }

    /// <summary>
    /// Zip every non-empty world under the instance's <c>saves/</c> and then prune to
    /// <see cref="AutoBackupKeepLatest"/>. Stream the per-zip line into the launcher log
    /// so the user sees what just got snapshotted. Failures are surfaced as <c>[warn]</c>
    /// lines but never abort the caller.
    /// </summary>
    private async Task BackupInstanceWorldsAsync(Instance instance, CancellationToken cancellationToken)
    {
        if (_backupService is null) return;
        try
        {
            // Fast-path: skip the whole dance when no worlds exist under saves/. Walking the
            // browser also keeps us symmetric with the Worlds tab UI - same source of truth.
            if (_instanceBrowser is not null)
            {
                var worlds = await _instanceBrowser.ListWorldsAsync(instance, cancellationToken).ConfigureAwait(false);
                if (worlds.Count == 0) return;
            }

            Append("[backup] Snapshotting worlds before launch ...");
            var produced = await _backupService.BackupAllWorldsAsync(instance, cancellationToken).ConfigureAwait(false);
            foreach (var e in produced)
            {
                var mb = e.SizeBytes / 1024.0 / 1024.0;
                var sizeLabel = mb >= 1
                    ? $"{mb.ToString("0.#", CultureInfo.InvariantCulture)} MB"
                    : $"{(e.SizeBytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";
                Append($"[backup] {Path.GetFileName(e.ArchivePath)} ({sizeLabel})");
            }
            if (produced.Count == 0)
            {
                // No worlds had any files - nothing zipped, nothing to prune.
                return;
            }
            if (_autoBackupKeepLatest > 0)
            {
                await _backupService.PruneAsync(instance, _autoBackupKeepLatest, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Courtesy, not a blocker - log and let the launch proceed.
            Append($"[warn] Pre-launch backup failed: {ex.Message}");
            _logger.Warn($"Pre-launch backup failed: {ex.Message}");
        }
    }

    private async Task RefreshInstanceCrashReportsAsync()
    {
        if (_crashReportListener is null || SelectedInstance is not { } inst) return;
        try
        {
            InstanceCrashReports = await _crashReportListener.ListRecentAsync(inst, 20, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list crash reports: {ex.Message}");
            InstanceCrashReports = Array.Empty<CrashReport>();
        }
    }

    private async Task RefreshInstanceResourcePacksAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            var list = await _instanceBrowser.ListResourcePacksAsync(inst, CancellationToken.None);
            ReplaceCollection(InstanceResourcePacks, list);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list resource packs: {ex.Message}");
            InstanceResourcePacks.Clear();
        }
    }

    private async Task RefreshInstanceShaderPacksAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            var list = await _instanceBrowser.ListShaderPacksAsync(inst, CancellationToken.None);
            ReplaceCollection(InstanceShaderPacks, list);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list shader packs: {ex.Message}");
            InstanceShaderPacks.Clear();
        }
    }

    private async Task RefreshInstanceDataPacksAsync()
    {
        if (_instanceBrowser is null || SelectedInstance is not { } inst) return;
        try
        {
            var list = await _instanceBrowser.ListDataPacksAsync(inst, CancellationToken.None);
            ReplaceCollection(InstanceDataPacks, list);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list data packs: {ex.Message}");
            InstanceDataPacks.Clear();
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, System.Collections.Generic.IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    /// <summary>
    /// Resolve the per-instance root directory (game directory or default <c>.minecraft</c>).
    /// Used by the pack toggle/remove logic to build absolute pack-file paths without going
    /// through the browser interface (which only enumerates - it doesn't expose the root).
    /// </summary>
    private string? ResolvePackRoot()
    {
        if (SelectedInstance is not { } inst) return null;
        return !string.IsNullOrWhiteSpace(inst.GameDirectory)
            ? inst.GameDirectory
            : Core.Installations.DefaultMinecraftInstallationLocator.ResolveRoot();
    }

    private async Task ToggleResourcePackAsync(string? filename)
    {
        if (string.IsNullOrEmpty(filename) || ResolvePackRoot() is not { } root) return;
        var dir = Path.Combine(root, "resourcepacks");
        ToggleZipDisabledSuffix(dir, filename);
        await RefreshInstanceResourcePacksAsync().ConfigureAwait(true);
    }

    private async Task ToggleShaderPackAsync(string? filename)
    {
        if (string.IsNullOrEmpty(filename) || ResolvePackRoot() is not { } root) return;
        var dir = Path.Combine(root, "shaderpacks");
        ToggleZipDisabledSuffix(dir, filename);
        await RefreshInstanceShaderPacksAsync().ConfigureAwait(true);
    }

    private async Task ToggleDataPackAsync(DataPackEntry? entry)
    {
        if (entry is null || ResolvePackRoot() is not { } root) return;
        var dir = Path.Combine(root, "saves", entry.WorldFolderName, "datapacks");
        ToggleZipDisabledSuffix(dir, entry.Filename);
        await RefreshInstanceDataPacksAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Rename a pack file between <c>foo.zip</c> and <c>foo.zip.disabled</c> in-place.
    /// Logs but never throws when the source is missing or the destination already exists -
    /// the UI just stays as it was.
    /// </summary>
    private void ToggleZipDisabledSuffix(string dir, string filename)
    {
        try
        {
            var source = Path.Combine(dir, filename);
            if (!File.Exists(source))
            {
                Append($"[warn] Pack file not found: {filename}");
                return;
            }
            string dest;
            if (filename.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
                dest = source.Substring(0, source.Length - ".disabled".Length);
            else if (filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                dest = source + ".disabled";
            else
                return;
            if (File.Exists(dest))
            {
                Append($"[warn] Cannot toggle '{filename}': '{Path.GetFileName(dest)}' already exists.");
                return;
            }
            File.Move(source, dest);
        }
        catch (Exception ex)
        {
            Append($"[error] Toggle pack '{filename}' failed: {ex.Message}");
        }
    }

    private async Task RemoveResourcePackAsync(string? filename)
    {
        if (string.IsNullOrEmpty(filename) || ResolvePackRoot() is not { } root) return;
        DeletePackFile(Path.Combine(root, "resourcepacks", filename));
        await RefreshInstanceResourcePacksAsync().ConfigureAwait(true);
    }

    private async Task RemoveShaderPackAsync(string? filename)
    {
        if (string.IsNullOrEmpty(filename) || ResolvePackRoot() is not { } root) return;
        DeletePackFile(Path.Combine(root, "shaderpacks", filename));
        await RefreshInstanceShaderPacksAsync().ConfigureAwait(true);
    }

    private async Task RemoveDataPackAsync(DataPackEntry? entry)
    {
        if (entry is null || ResolvePackRoot() is not { } root) return;
        DeletePackFile(Path.Combine(root, "saves", entry.WorldFolderName, "datapacks", entry.Filename));
        await RefreshInstanceDataPacksAsync().ConfigureAwait(true);
    }

    private void DeletePackFile(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not delete '{Path.GetFileName(fullPath)}': {ex.Message}");
        }
    }

    /// <summary>
    /// Open the first suspect mod's Modrinth search page in the OS default browser. Falls back to
    /// the CurseForge link when Modrinth is empty. No-op when nothing is selected or no suspects.
    /// </summary>
    private Task OpenSelectedCrashReportInBrowserAsync()
    {
        if (SelectedCrashReport is null || SelectedCrashReport.SuspectedMods.Count == 0)
            return Task.CompletedTask;
        var url = SelectedCrashReport.SuspectedMods[0].ModrinthSearchUrl
                  ?? SelectedCrashReport.SuspectedMods[0].CurseForgeSearchUrl;
        if (string.IsNullOrEmpty(url)) return Task.CompletedTask;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Append($"[error] Could not open browser: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private async Task RefreshAllInstanceTabsAsync(Instance instance)
    {
        if (_instanceBrowser is null) return;
        try
        {
            var ssTask = _instanceBrowser.ListScreenshotsAsync(instance, CancellationToken.None);
            var worldsTask = _instanceBrowser.ListWorldsAsync(instance, CancellationToken.None);
            var serversTask = _instanceBrowser.ListServersAsync(instance, CancellationToken.None);
            var resTask = _instanceBrowser.ListResourcePacksAsync(instance, CancellationToken.None);
            var shaderTask = _instanceBrowser.ListShaderPacksAsync(instance, CancellationToken.None);
            var dataTask = _instanceBrowser.ListDataPacksAsync(instance, CancellationToken.None);
            await Task.WhenAll(ssTask, worldsTask, serversTask, resTask, shaderTask, dataTask);
            // Guard against the selection moving while we were scanning.
            if (!ReferenceEquals(SelectedInstance, instance)) return;
            InstanceScreenshots = ssTask.Result;
            InstanceWorlds = worldsTask.Result;
            InstanceServers = serversTask.Result;
            ReplaceCollection(InstanceResourcePacks, resTask.Result);
            ReplaceCollection(InstanceShaderPacks, shaderTask.Result);
            ReplaceCollection(InstanceDataPacks, dataTask.Result);
        }
        catch (Exception ex)
        {
            Append($"[error] Could not load instance browser: {ex.Message}");
        }
    }

    // ---- Mods page logic ----

    private IModRepository? GetActiveModRepository() => SelectedModSource switch
    {
        ModSource.Modrinth => _modrinthRepository,
        ModSource.CurseForge => _curseForgeRepository,
        _ => null,
    };

    private async Task SearchModsAsync()
    {
        var repo = GetActiveModRepository();
        if (repo is null)
        {
            Append(Strings.Log_ModsNoRepository);
            return;
        }
        IsBusy = true;
        try
        {
            Append(string.Format(Strings.Log_ModsSearching, SelectedModSource, ModSearchTerm));
            var query = new ModSearchQuery
            {
                Query = ModSearchTerm,
                Limit = 30,
                GameVersion = SelectedInstance?.VersionId,
                Loader = SelectedInstance?.Loader,
            };
            var hits = await repo.SearchAsync(query, CancellationToken.None);
            ModSearchResults.Clear();
            foreach (var m in hits)
                ModSearchResults.Add(m);
            Append(string.Format(Strings.Log_ModsResultsCount, hits.Count));
        }
        catch (Exception ex)
        {
            Append($"[error] Mod search failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallSelectedModAsync()
    {
        if (SelectedModSearchResult is not { } mod || SelectedInstance is not { } inst || _instanceModManager is null)
            return;
        var repo = GetActiveModRepository() ?? (mod.Source switch
        {
            ModSource.Modrinth => _modrinthRepository,
            ModSource.CurseForge => _curseForgeRepository,
            _ => null,
        });
        if (repo is null)
        {
            Append(Strings.Log_ModsNoRepositoryForInstall);
            return;
        }
        IsBusy = true;
        try
        {
            Append(string.Format(Strings.Log_ModsResolvingFiles, mod.Name, inst.VersionId, inst.Loader));
            var files = await repo.ListFilesAsync(mod.Id, inst.VersionId, inst.Loader == ModLoader.None ? null : inst.Loader, CancellationToken.None);
            if (files.Count == 0)
            {
                Append(Strings.Log_ModsNoMatchingFiles);
                return;
            }
            var file = files[0];
            Append(string.Format(Strings.Log_ModsInstalling, file.Filename ?? file.DisplayName, inst.Name));
            await _instanceModManager.InstallAsync(inst, file, repo, CancellationToken.None);
            await RefreshInstalledModsAsync();
            Append(Strings.Log_ModsInstallComplete);
        }
        catch (Exception ex)
        {
            Append($"[error] Install failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshInstalledModsAsync()
    {
        if (SelectedInstance is not { } inst || _instanceModManager is null) return;
        IsBusy = true;
        try
        {
            var list = await _instanceModManager.ListInstalledAsync(inst, CancellationToken.None);
            InstalledMods.Clear();
            foreach (var m in list)
                InstalledMods.Add(m);
            Append(string.Format(Strings.Log_ModsInstalledInInstance, list.Count, inst.Name));
        }
        catch (Exception ex)
        {
            Append($"[error] Could not list installed mods: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleSelectedInstalledModAsync()
    {
        if (SelectedInstalledMod is not { } victim || SelectedInstance is not { } inst || _instanceModManager is null)
            return;
        IsBusy = true;
        try
        {
            await _instanceModManager.SetEnabledAsync(inst, victim.Filename, enabled: !victim.IsEnabled, CancellationToken.None);
            await RefreshInstalledModsAsync();
        }
        catch (Exception ex)
        {
            Append($"[error] Toggle failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveSelectedInstalledModAsync()
    {
        if (SelectedInstalledMod is not { } victim || SelectedInstance is not { } inst || _instanceModManager is null)
            return;
        IsBusy = true;
        try
        {
            await _instanceModManager.RemoveAsync(inst, victim.Filename, CancellationToken.None);
            await RefreshInstalledModsAsync();
            Append(string.Format(Strings.Log_ModsRemoved, victim.Filename));
        }
        catch (Exception ex)
        {
            Append($"[error] Remove failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Append a line to <see cref="LogText"/> (visible to tests).</summary>
    public void Append(string line)
    {
        var stamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LogText = string.IsNullOrEmpty(LogText)
            ? $"{stamp}  {line}"
            : $"{LogText}{Environment.NewLine}{stamp}  {line}";
    }

    /// <summary>
    /// Bridge a live <see cref="GameLogLine"/> stream into the launcher's in-window log
    /// (the <see cref="LogText"/> buffer). Each stdout/stderr line is prefixed with
    /// <c>[game]</c> so users can tell game output apart from the launcher's own messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned <see cref="Task"/> completes when the observable fires <c>OnCompleted</c>
    /// (the underlying launcher does so from <c>Process.Exited</c>), at which point the helper
    /// logs a closing marker line and disposes the subscription. <c>public</c> rather than
    /// <c>internal</c> so the Core test project can drive it without an
    /// <c>InternalsVisibleTo</c>; the same exposure used by <see cref="Append"/>.
    /// </para>
    /// <para>
    /// The underlying launcher raises <c>OutputDataReceived</c>/<c>ErrorDataReceived</c> on
    /// background threads. Our <see cref="LogText"/> setter notifies bindings via the
    /// <see cref="System.ComponentModel.INotifyPropertyChanged"/> contract which Avalonia
    /// dispatches to the UI thread internally, so the bridge stays correct without an
    /// explicit thread hop on this side. The unit tests exercise both the subscribe and the
    /// complete-on-OnCompleted paths.
    /// </para>
    /// </remarks>
    public Task SubscribeToGameLogAsync(IObservable<GameLogLine> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var tcs = new TaskCompletionSource();
        IDisposable? sub = null;
        sub = stream.Subscribe(
            onNext: line => Append($"[game] {line.Text}"),
            onCompleted: () =>
            {
                Append("[game] (process exited)");
                sub?.Dispose();
                tcs.TrySetResult();
            });
        return tcs.Task;
    }

    // ---- Logs page ----

    /// <summary>Raw text of today's daily-rotated launcher log file.</summary>
    /// <remarks>
    /// Distinct from <see cref="LogText"/>: that one streams the running launch session into the
    /// Home page's textbox; this one mirrors the on-disk file at the moment of the last refresh.
    /// </remarks>
    public string LogFileText
    {
        get => _logFileText;
        private set
        {
            if (SetField(ref _logFileText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FilteredLogText));
                OnPropertyChanged(nameof(LogLineCount));
                OnPropertyChanged(nameof(LogStatusText));
            }
        }
    }

    /// <summary>User-typed filter substring; empty = show everything.</summary>
    public string LogFilter
    {
        get => _logFilter;
        set
        {
            if (SetField(ref _logFilter, value ?? string.Empty))
                OnPropertyChanged(nameof(FilteredLogText));
        }
    }

    /// <summary>The log file content filtered by <see cref="LogFilter"/> (case-insensitive substring).</summary>
    public string FilteredLogText => Core.Logging.LogFiltering.Filter(_logFileText, _logFilter);

    /// <summary>Total number of non-empty lines in the loaded log file.</summary>
    public int LogLineCount
    {
        get
        {
            if (string.IsNullOrEmpty(_logFileText)) return 0;
            // Count by splitting on either CRLF or bare LF, ignoring trailing blanks - matches what the user actually sees.
            return _logFileText
                .Split('\n')
                .Count(l => !string.IsNullOrWhiteSpace(l));
        }
    }

    /// <summary>Bottom-status line shown under the log viewer: "{n} lines · last refreshed {hh:mm:ss}".</summary>
    public string LogStatusText
    {
        get
        {
            var stamp = _logLastRefreshed == default
                ? "never"
                : _logLastRefreshed.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            // Interpunct (U+00B7) keeps this readable without the heavy "neuron stripe" em-dash.
            return $"{LogLineCount} lines · last refreshed {stamp}";
        }
    }

    /// <summary>
    /// Read the contents of today's daily-rotated launcher log from disk and push them onto
    /// <see cref="LogFileText"/>. Wired to the Logs-page Refresh button and called when the user
    /// navigates to the Logs section so the page is never blank.
    /// </summary>
    public async Task LoadTodaysLogAsync()
    {
        IsBusy = true;
        try
        {
            var dir = DefaultLogDirectory.Resolve();
            var path = Path.Combine(dir, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");

            if (!File.Exists(path))
            {
                LogFileText = $"(no log file yet at {path})";
            }
            else
            {
                // FileShare.ReadWrite so we don't fight the live FileLauncherLogger that may be appending.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                LogFileText = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            _logLastRefreshed = DateTime.Now;
            OnPropertyChanged(nameof(LogStatusText));
        }
        catch (Exception ex)
        {
            LogFileText = $"(could not read log file: {ex.Message})";
            _logLastRefreshed = DateTime.Now;
            OnPropertyChanged(nameof(LogStatusText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Launcher self-update banner (T16) ----

    /// <summary>
    /// Latest update reported by the GitHub Releases probe (strictly newer than the running
    /// build), or <c>null</c> when no update is available / probe was disabled / the user
    /// dismissed the banner. INPC-bound; the View shows the banner when this is non-null.
    /// </summary>
    public UpdateInfo? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (SetField(ref _availableUpdate, value))
            {
                OnPropertyChanged(nameof(HasUpdateAvailable));
                OnPropertyChanged(nameof(UpdateBannerText));
                OpenReleasePageCommand.RaiseCanExecuteChanged();
                DismissUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ---- Headless servers (v0.28 T11) ----

    /// <summary>Saved headless dedicated servers, newest-first.</summary>
    public ObservableCollection<HeadlessServer> HeadlessServers { get; }

    /// <summary>The server selected in the headless servers list, or <c>null</c> when nothing is highlighted.</summary>
    public HeadlessServer? SelectedHeadlessServer
    {
        get => _selectedHeadlessServer;
        set
        {
            if (SetField(ref _selectedHeadlessServer, value))
            {
                DeleteHeadlessServerCommand.RaiseCanExecuteChanged();
                StartHeadlessServerCommand.RaiseCanExecuteChanged();
                StopHeadlessServerCommand.RaiseCanExecuteChanged();
                SendHeadlessCommand?.RaiseCanExecuteChanged();
                // Swap the bound console buffer so flipping selection shows the right server's
                // stdout. New entries get a fresh empty collection so the XAML doesn't ride on null.
                if (value is not null)
                {
                    if (!_headlessConsoleLines.TryGetValue(value.Id, out var buffer))
                    {
                        buffer = new ObservableCollection<string>();
                        _headlessConsoleLines[value.Id] = buffer;
                    }
                    HeadlessConsoleLines = buffer;
                    // If the orchestrator is already running this server (e.g. selection lost
                    // and re-gained, or a list refresh), re-hook the subscription.
                    if (_headlessServerOrchestrator?.GetState(value.Id) is
                        HeadlessServerState.Running or HeadlessServerState.Starting)
                    {
                        SubscribeConsoleLines(value.Id);
                    }
                }
                else
                {
                    HeadlessConsoleLines = new ObservableCollection<string>();
                }
                OnPropertyChanged(nameof(HeadlessConsoleLines));
                OnPropertyChanged(nameof(HeadlessSelectedStatus));
                OnPropertyChanged(nameof(IsHeadlessConsoleVisible));
            }
        }
    }

    /// <summary>
    /// Whether the console pane should be shown. True when a server is selected AND the
    /// orchestrator is wired (i.e. not a test-mode build). The pane is shown even when
    /// Stopped so the user can see the stdout history from the last run.
    /// </summary>
    public bool IsHeadlessConsoleVisible =>
        _headlessServerOrchestrator is not null && SelectedHeadlessServer is not null;

    /// <summary>Convenience helper for the XAML banner's <c>IsVisible</c> binding.</summary>
    public bool HasUpdateAvailable => _availableUpdate is not null;

    /// <summary>Single-line banner caption: "Launcher update available: v0.99.0 - click to open release page".</summary>
    public string UpdateBannerText => _availableUpdate is null
        ? string.Empty
        : $"Launcher update available: v{_availableUpdate.LatestVersion} · click to open release page";

    /// <summary>Click on the banner opens <see cref="UpdateInfo.ReleaseUrl"/> in the default browser.</summary>
    public AsyncRelayCommand OpenReleasePageCommand { get; }

    /// <summary>X button on the banner clears <see cref="AvailableUpdate"/> for this session.</summary>
    public AsyncRelayCommand DismissUpdateCommand { get; }

    /// <summary>
    /// Bug 5 (v0.32.0): "Check for updates" button on the Settings page. Runs the
    /// same probe the startup refresh runs, but on demand. Sets
    /// <see cref="AvailableUpdate"/> on a hit (banner appears) or appends a "you're
    /// on the latest version" log line on a miss, so the user always gets feedback.
    /// </summary>
    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateChecker is null)
        {
            Append("[update] Update checker is not configured in this build.");
            return;
        }

        try
        {
            Append($"Checking for updates (current: v{CurrentLauncherVersion}) ...");
            var info = await _updateChecker.CheckAsync(CurrentLauncherVersion, CancellationToken.None).ConfigureAwait(true);
            if (info is null)
            {
                Append($"[update] You're on the latest version (v{CurrentLauncherVersion}).");
            }
            else
            {
                AvailableUpdate = info;
                Append($"A newer launcher version is available: v{info.LatestVersion}");
            }
        }
        catch (Exception ex)
        {
            // The checker contract says it shouldn't throw; defend against future drift anyway.
            _logger.Warn($"Manual update check failed: {ex.Message}");
            Append($"[update] Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Bug 3 (v0.32.0): X button on the per-instance detail panel. Nulls
    /// <see cref="SelectedInstance"/>, which collapses the panel via the
    /// <see cref="HasSelectedInstance"/> binding. Surfaced from the panel's top-right
    /// corner next to the tab strip.
    /// </summary>
    public AsyncRelayCommand CloseInstanceDetailCommand { get; }

    private Task CloseInstanceDetailAsync()
    {
        SelectedInstance = null;
        return Task.CompletedTask;
    }

    private Task OpenReleasePageAsync()
    {
        var info = _availableUpdate;
        if (info is null || string.IsNullOrWhiteSpace(info.ReleaseUrl)) return Task.CompletedTask;

        try
        {
            // UseShellExecute=true forces the default browser to handle the http(s) scheme
            // on every supported OS; .NET 6+ would otherwise default to false on .NET Core.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleaseUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Cosmetic: log and keep the banner up so the user knows the link is still there.
            _logger.Warn($"Could not open release page: {ex.Message}");
            Append($"[warn] Could not open release page: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private Task DismissUpdateAsync()
    {
        AvailableUpdate = null;
        return Task.CompletedTask;
    }

    /// <summary>Reload the list from the store. Bound to the page's Refresh button.</summary>
    public AsyncRelayCommand RefreshHeadlessServersCommand { get; }

    /// <summary>Delete the currently-selected server (folder + metadata).</summary>
    public AsyncRelayCommand DeleteHeadlessServerCommand { get; }

    /// <summary>Placeholder until the v0.29 server-jar pipeline lands - logs a "not implemented yet" line.</summary>
    public AsyncRelayCommand StartHeadlessServerCommand { get; }

    /// <summary>Placeholder until the v0.29 server-jar pipeline lands - logs a "not implemented yet" line.</summary>
    public AsyncRelayCommand StopHeadlessServerCommand { get; }

    /// <summary>
    /// Helper invoked by the New Server dialog: writes the entry through the store, then
    /// pushes it onto the bound collection so the page reflects the creation immediately
    /// without a full reload.
    /// </summary>
    public async Task<HeadlessServer?> CreateHeadlessServerAsync(HeadlessServerCreateRequest request)
    {
        if (_headlessServerStore is null) return null;
        try
        {
            IsBusy = true;
            var server = await _headlessServerStore.CreateAsync(request, CancellationToken.None);
            HeadlessServers.Insert(0, server);
            SelectedHeadlessServer = server;
            _logger.Info($"Created headless server '{server.Name}' ({server.VersionId}) at {server.Path}.");
            return server;
        }
        catch (Exception ex)
        {
            _logger.Error("Could not create headless server.", ex);
            Append($"Could not create headless server: {ex.Message}");
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshHeadlessServersAsync()
    {
        if (_headlessServerStore is null) return;
        try
        {
            IsBusy = true;
            var list = await _headlessServerStore.ListAsync(CancellationToken.None);
            HeadlessServers.Clear();
            foreach (var s in list)
                HeadlessServers.Add(s);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not list headless servers.", ex);
            Append($"Could not list headless servers: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedHeadlessServerAsync()
    {
        if (_headlessServerStore is null || SelectedHeadlessServer is null) return;
        try
        {
            IsBusy = true;
            var target = SelectedHeadlessServer;
            await _headlessServerStore.DeleteAsync(target.Id, CancellationToken.None);
            HeadlessServers.Remove(target);
            SelectedHeadlessServer = null;
            _logger.Info($"Deleted headless server '{target.Name}' ({target.Id}).");
        }
        catch (Exception ex)
        {
            _logger.Error("Could not delete headless server.", ex);
            Append($"Could not delete headless server: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // v0.32.1: real Start. Downloads server.jar via the orchestrator (which goes through the
    // Mojang manifest), ensures the matching Java runtime is on disk, and spawns the JVM.
    // The stdout stream is subscribed at the same moment so the console pane gets every line.
    private async Task StartSelectedHeadlessServerAsync()
    {
        var server = SelectedHeadlessServer;
        if (server is null || _headlessServerOrchestrator is null) return;

        try
        {
            IsBusy = true;
            _logger.Info($"StartHeadlessServer: {server.Name} ({server.VersionId})");
            Append($"Starting headless server '{server.Name}' ({server.VersionId})...");
            await _headlessServerOrchestrator.StartAsync(server, CancellationToken.None).ConfigureAwait(true);
            // Hook the line stream so the console pane updates live (caps at HeadlessConsoleLineCap).
            SubscribeConsoleLines(server.Id);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not start headless server.", ex);
            Append($"Could not start headless server: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopSelectedHeadlessServerAsync()
    {
        var server = SelectedHeadlessServer;
        if (server is null || _headlessServerOrchestrator is null) return;

        try
        {
            IsBusy = true;
            _logger.Info($"StopHeadlessServer: {server.Name}");
            Append($"Stopping headless server '{server.Name}'...");
            await _headlessServerOrchestrator.StopAsync(server.Id, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not stop headless server.", ex);
            Append($"Could not stop headless server: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendHeadlessCommandAsync()
    {
        var server = SelectedHeadlessServer;
        if (server is null || _headlessServerOrchestrator is null) return;
        var command = _headlessCommandInput?.Trim();
        if (string.IsNullOrEmpty(command)) return;

        try
        {
            await _headlessServerOrchestrator.SendCommandAsync(server.Id, command, CancellationToken.None).ConfigureAwait(true);
            HeadlessCommandInput = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Warn($"SendHeadlessCommand failed: {ex.Message}");
            Append($"Could not send command: {ex.Message}");
        }
    }

    /// <summary>The text in the headless console "command" entry box, two-way bound from XAML.</summary>
    public string HeadlessCommandInput
    {
        get => _headlessCommandInput;
        set
        {
            if (SetField(ref _headlessCommandInput, value ?? string.Empty))
                SendHeadlessCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Send the buffered command to the running server's stdin and clear the box.</summary>
    public AsyncRelayCommand SendHeadlessCommand { get; private set; } = null!;

    /// <summary>Stdout lines for the currently-selected headless server. Empty when no server
    /// has been started yet. Capped at <see cref="HeadlessConsoleLineCap"/>.</summary>
    public ObservableCollection<string> HeadlessConsoleLines { get; private set; } = new();

    /// <summary>
    /// Per-server status string used by the "Stopped / Starting... / Running (pid X) / Stopping..." label
    /// next to the Start/Stop buttons. Resolves from the orchestrator's live state.
    /// </summary>
    public string HeadlessSelectedStatus
    {
        get
        {
            var s = SelectedHeadlessServer;
            if (s is null || _headlessServerOrchestrator is null) return "Stopped";
            return _headlessServerOrchestrator.GetState(s.Id) switch
            {
                HeadlessServerState.Starting => "Starting...",
                HeadlessServerState.Running => $"Running (pid {_headlessServerOrchestrator.GetProcessId(s.Id)})",
                HeadlessServerState.Stopping => "Stopping...",
                _ => "Stopped",
            };
        }
    }

    /// <summary>
    /// Wire the orchestrator's stdout subject for <paramref name="serverId"/> into the bound
    /// <see cref="HeadlessConsoleLines"/> collection. Idempotent - re-subscribing on the same
    /// server is a no-op, so flipping back to it after a list refresh doesn't duplicate lines.
    /// </summary>
    private void SubscribeConsoleLines(string serverId)
    {
        if (_headlessServerOrchestrator is null) return;
        if (_headlessConsoleSubscriptions.ContainsKey(serverId)) return;
        var stream = _headlessServerOrchestrator.GetStdoutLines(serverId);
        if (stream is null) return;

        if (!_headlessConsoleLines.TryGetValue(serverId, out var buffer))
        {
            buffer = new ObservableCollection<string>();
            _headlessConsoleLines[serverId] = buffer;
        }

        var observer = new ConsoleLineObserver(this, serverId);
        _headlessConsoleSubscriptions[serverId] = stream.Subscribe(observer);

        if (SelectedHeadlessServer?.Id == serverId)
        {
            HeadlessConsoleLines = buffer;
            OnPropertyChanged(nameof(HeadlessConsoleLines));
        }
    }

    private void OnHeadlessOrchestratorStateChanged(object? sender, HeadlessServerStateChange e)
    {
        // Hop to the UI thread so collection mutations + INPC stay on the right context.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // When a process exits on its own (state -> Stopped via the exit observer) we drop
            // the subscription so a subsequent restart re-hooks cleanly.
            if (e.NewState == HeadlessServerState.Stopped &&
                _headlessConsoleSubscriptions.TryGetValue(e.ServerId, out var sub))
            {
                try { sub.Dispose(); } catch { /* best-effort */ }
                _headlessConsoleSubscriptions.Remove(e.ServerId);
            }

            if (SelectedHeadlessServer?.Id == e.ServerId)
                OnPropertyChanged(nameof(HeadlessSelectedStatus));

            StartHeadlessServerCommand.RaiseCanExecuteChanged();
            StopHeadlessServerCommand.RaiseCanExecuteChanged();
            SendHeadlessCommand.RaiseCanExecuteChanged();
        });
    }

    private void AppendHeadlessConsoleLine(string serverId, string line)
    {
        if (!_headlessConsoleLines.TryGetValue(serverId, out var buffer)) return;
        // Trim from the head when we reach the cap so the buffer stays bounded on long runs.
        if (buffer.Count >= HeadlessConsoleLineCap)
            buffer.RemoveAt(0);
        buffer.Add(line);
    }

    private sealed class ConsoleLineObserver : IObserver<string>
    {
        private readonly MainViewModel _vm;
        private readonly string _serverId;
        public ConsoleLineObserver(MainViewModel vm, string serverId)
        {
            _vm = vm;
            _serverId = serverId;
        }
        public void OnNext(string value)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _vm.AppendHeadlessConsoleLine(_serverId, value));
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Settings-page locale dropdown row. <see cref="Culture"/> is the BCP 47 tag the launcher
/// persists into <c>LauncherSettings.Locale</c> (<c>null</c> = "Use system default");
/// <see cref="DisplayName"/> is the user-visible label, typically the culture's native name.
/// </summary>
public sealed record LocaleOption(string? Culture, string DisplayName)
{
    /// <summary>Used by the ComboBox to render the row label.</summary>
    public override string ToString() => DisplayName;
}
