using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;
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

    // Settings-page state (mirrored from LauncherSettings on load, written back on Save).
    private int _minMemoryMb;
    private int _maxMemoryMb;
    private string _jvmArguments = string.Empty;
    private string _gameDirectoryOverride = string.Empty;
    private string _javaExecutableOverride = string.Empty;
    private bool _keepLauncherOpen;
    private bool _showGameLog;
    private bool _sidebarCollapsed;
    private readonly int _maxAllowedMemoryMb;

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

    // Logs page state: the full contents of today's launcher log file, the user-typed filter,
    // and the timestamp of the last successful refresh. FilteredLogText is recomputed on demand
    // from LogFileText + LogFilter through the LogFiltering helper.
    private string _logFileText = string.Empty;
    private string _logFilter = string.Empty;
    private DateTime _logLastRefreshed;

    /// <summary>Construct with the launcher service and (optional) Microsoft auth provider + settings store.</summary>
    public MainViewModel(
        IMinecraftLauncherService service,
        ILauncherLogger logger,
        IMicrosoftAuthService? microsoftAuth = null,
        ILauncherSettingsStore? settingsStore = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _microsoftAuth = microsoftAuth;
        _settingsStore = settingsStore;
        _maxAllowedMemoryMb = SystemRam.RecommendedMaxHeapMb();

        // MSAL device-code prompts come from a background thread; surface them in the UI log.
        if (_microsoftAuth is not null)
            _microsoftAuth.DeviceCodeRequested += OnDeviceCodeRequested;

        // Load persisted settings synchronously - the file is small. Defaults if absent / corrupt.
        var initial = settingsStore is null
            ? new LauncherSettings()
            : settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        ApplySettings(initial);

        AvailableVersions = new ObservableCollection<VersionMetadata>();
        FilteredVersions = new ObservableCollection<VersionMetadata>();
        InstalledVersions = new ObservableCollection<InstalledVersion>();
        Profiles = new ObservableCollection<LauncherProfile>();
        Servers = new ObservableCollection<ServerListEntry>();
        News = new ObservableCollection<NewsEntry>();
        Instances = new ObservableCollection<Instance>();

        RefreshVersionsCommand = new AsyncRelayCommand(RefreshVersionsAsync, () => !IsBusy);
        RefreshInstalledVersionsCommand = new AsyncRelayCommand(RefreshInstalledVersionsAsync, () => !IsBusy);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync, () => !IsBusy);
        RefreshServersCommand = new AsyncRelayCommand(RefreshServersAsync, () => !IsBusy);
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

    /// <summary>Multiplayer servers parsed from <c>servers.dat</c>.</summary>
    public ObservableCollection<ServerListEntry> Servers { get; }

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
            }
        }
    }

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
    };

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
                RefreshNewsCommand.RaiseCanExecuteChanged();
                RefreshInstancesCommand.RaiseCanExecuteChanged();
                DeleteInstanceCommand.RaiseCanExecuteChanged();
                SaveSettingsCommand.RaiseCanExecuteChanged();
                LaunchCommand.RaiseCanExecuteChanged();
                SignInMicrosoftCommand.RaiseCanExecuteChanged();
                SignOutCommand.RaiseCanExecuteChanged();
                RefreshLogFileCommand.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsNewsSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
                OnPropertyChanged(nameof(IsLogsSelected));

                // Auto-load today's log file the first time the user opens the Logs page so
                // it isn't blank. The Refresh button re-reads it on demand after that.
                if (value == NavSection.Logs)
                    _ = LoadTodaysLogAsync();
            }
        }
    }

    public bool IsHomeSelected => SelectedSection == NavSection.Home;
    public bool IsInstallationsSelected => SelectedSection == NavSection.Installations;
    public bool IsSkinsSelected => SelectedSection == NavSection.Skins;
    public bool IsServersSelected => SelectedSection == NavSection.Servers;
    public bool IsNewsSelected => SelectedSection == NavSection.News;
    public bool IsSettingsSelected => SelectedSection == NavSection.Settings;
    public bool IsLogsSelected => SelectedSection == NavSection.Logs;

    // ---- Commands ----

    public AsyncRelayCommand RefreshVersionsCommand { get; }
    public AsyncRelayCommand RefreshInstalledVersionsCommand { get; }
    public AsyncRelayCommand RefreshProfilesCommand { get; }
    public AsyncRelayCommand RefreshServersCommand { get; }
    public AsyncRelayCommand RefreshNewsCommand { get; }
    public AsyncRelayCommand RefreshInstancesCommand { get; }
    public AsyncRelayCommand DeleteInstanceCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand SignInMicrosoftCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }
    public AsyncRelayCommand RefreshLogFileCommand { get; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task RefreshVersionsAsync()
    {
        IsBusy = true;
        try
        {
            Append("Loading version manifest ...");
            var versions = await _service.ListVersionsAsync(CancellationToken.None);

            AvailableVersions.Clear();
            foreach (var v in versions)
                AvailableVersions.Add(v);

            // Rebuild the filtered view (and re-evaluate the SelectedVersion fallback)
            // now that the source list has new entries.
            ApplyVersionFilter();

            Append($"Loaded {versions.Count} versions.");
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
        // 0) If MSAL still holds a refresh token from the last session, restore the user
        //    silently so they don't have to re-enter a device code on every launch.
        if (_microsoftAuth is { HasCachedAccount: true })
        {
            try
            {
                Append("Silent Microsoft sign-in (cached refresh token) ...");
                CurrentSession = await _microsoftAuth.SignInSilentlyAsync(CancellationToken.None);
                Append($"Auto-signed in as '{CurrentSession.Username}'.");
            }
            catch (Exception ex)
            {
                // Refresh token expired or no cache - leave the user signed out, they can
                // click Sign in manually to trigger the device-code flow.
                Append($"Silent sign-in skipped: {ex.Message}");
            }
        }

        Append("Auto-refreshing on startup ...");
        await RefreshInstancesAsync();
        await RefreshProfilesAsync();
        await RefreshServersAsync();
        await RefreshNewsAsync();
        await RefreshVersionsAsync();
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
            Append("Loading instances ...");
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
            Append("Cannot delete an auto-imported instance (it lives under .minecraft/versions/).");
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
    public async Task<Instance> CreateInstanceAsync(string name, string versionId, string iconKey)
    {
        var instance = new Instance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            VersionId = versionId,
            IconKey = iconKey,
        };
        await _service.SaveInstanceAsync(instance, CancellationToken.None);
        Instances.Insert(0, instance);
        Append($"Created instance '{name}' for version {versionId}.");
        return instance;
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
            Append("Cannot change the icon of an auto-imported instance.");
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

    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null) return;
        IsBusy = true;
        try
        {
            Append("Saving settings ...");
            await _settingsStore.SaveAsync(BuildSettings(), CancellationToken.None);
            Append($"Settings saved (Xms={MinMemoryMb}M, Xmx={MaxMemoryMb}M).");
        }
        catch (Exception ex)
        {
            Append($"[error] Could not save settings: {ex.Message}");
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
            Append("Fetching Minecraft news ...");
            var news = await _service.ListNewsAsync(CancellationToken.None);

            News.Clear();
            foreach (var n in news)
                News.Add(n);

            Append($"Loaded {news.Count} news articles.");
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
            Append("Reading servers.dat ...");
            var servers = await _service.ListServersAsync(CancellationToken.None);

            Servers.Clear();
            foreach (var s in servers)
                Servers.Add(s);

            Append($"Loaded {servers.Count} servers.");
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

    private async Task RefreshProfilesAsync()
    {
        IsBusy = true;
        try
        {
            Append("Reading launcher_profiles.json ...");
            var profiles = await _service.ListProfilesAsync(CancellationToken.None);

            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(p);

            Append($"Loaded {profiles.Count} profiles from launcher_profiles.json.");
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
            Append("Scanning installed versions ...");
            var versions = await _service.ListInstalledVersionsAsync(CancellationToken.None);

            InstalledVersions.Clear();
            foreach (var v in versions)
                InstalledVersions.Add(v);

            Append($"Found {versions.Count} installed versions.");
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
            Append($"Microsoft sign-in: enter code {info.UserCode} at {info.VerificationUrl}");
            DeviceCodeRequested?.Invoke(this, info);
        });
    }

    private async Task SignInMicrosoftAsync()
    {
        IsBusy = true;
        try
        {
            Append("Signing in with Microsoft ...");
            var auth = await _service.AuthenticateAsync(
                new AuthRequest { Mode = AuthMode.Microsoft, Username = string.Empty },
                CancellationToken.None);

            CurrentSession = auth;
            Append($"Signed in as '{auth.Username}'.");
            _logger.Info($"UI: Microsoft sign-in succeeded for '{auth.Username}'.");
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
            Append("Signed out.");
            _logger.Info("UI: signed out.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LaunchAsync()
    {
        // Priority: explicit Instance > already-installed version > manifest version.
        var versionName = SelectedInstance?.VersionId ?? SelectedInstalledVersion?.Id ?? SelectedVersion?.Name;
        if (string.IsNullOrEmpty(versionName))
            return;

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

            Append($"Launching {versionName} (Xms={MinMemoryMb}M, Xmx={MaxMemoryMb}M) ...");
            var result = await _service.LaunchAsync(
                new LaunchRequest
                {
                    VersionName = versionName,
                    Session = auth,
                    GameDirectory = string.IsNullOrWhiteSpace(_gameDirectoryOverride) ? null : _gameDirectoryOverride,
                    MinimumRamMb = MinMemoryMb,
                    MaximumRamMb = MaxMemoryMb,
                },
                progress,
                CancellationToken.None);

            Append($"Launched. pid={result.ProcessId} version={result.VersionName}");
            _logger.Info($"UI: launch complete (pid {result.ProcessId}, version {result.VersionName}).");

            // Mark "last played" on the running instance so the grid sorts it to the front next time.
            // Auto-imported instances aren't in our store - just refresh their in-memory copy
            // so the UI reacts, without persisting.
            if (SelectedInstance is { } inst)
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

    /// <summary>Append a line to <see cref="LogText"/> (visible to tests).</summary>
    public void Append(string line)
    {
        var stamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LogText = string.IsNullOrEmpty(LogText)
            ? $"{stamp}  {line}"
            : $"{LogText}{Environment.NewLine}{stamp}  {line}";
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
