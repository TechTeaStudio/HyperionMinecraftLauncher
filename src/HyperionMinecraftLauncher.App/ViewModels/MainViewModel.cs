using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
    private readonly int _maxAllowedMemoryMb;

    private string _username = "Steve";
    private VersionMetadata? _selectedVersion;
    private InstalledVersion? _selectedInstalledVersion;
    private LauncherProfile? _selectedProfile;
    private Instance? _selectedInstance;
    private string _logText = string.Empty;
    private bool _isBusy;
    private AuthResult? _currentSession;
    private NavSection _selectedSection = NavSection.Home;
    private Bitmap? _avatarBitmap;

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
        DeleteInstanceCommand = new AsyncRelayCommand(DeleteSelectedInstanceAsync, () => !IsBusy && SelectedInstance is not null);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy && _settingsStore is not null);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanLaunch);
        SignInMicrosoftCommand = new AsyncRelayCommand(SignInMicrosoftAsync, () => !IsBusy && !IsSignedInOnline);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, () => !IsBusy && IsSignedInOnline);
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

    private void ApplySettings(LauncherSettings s)
    {
        _maxMemoryMb = Math.Clamp(s.MaximumRamMb, 512, _maxAllowedMemoryMb);
        _minMemoryMb = Math.Clamp(s.MinimumRamMb, 256, _maxMemoryMb);
        _jvmArguments = s.JvmArguments ?? string.Empty;
        _gameDirectoryOverride = s.GameDirectory ?? string.Empty;
        _javaExecutableOverride = s.JavaExecutable ?? string.Empty;
        _keepLauncherOpen = s.KeepLauncherOpen;
        _showGameLog = s.ShowGameLog;
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
            }
        }
    }

    public bool IsHomeSelected => SelectedSection == NavSection.Home;
    public bool IsInstallationsSelected => SelectedSection == NavSection.Installations;
    public bool IsSkinsSelected => SelectedSection == NavSection.Skins;
    public bool IsServersSelected => SelectedSection == NavSection.Servers;
    public bool IsNewsSelected => SelectedSection == NavSection.News;
    public bool IsSettingsSelected => SelectedSection == NavSection.Settings;

    // ---- Commands ----

    public AsyncRelayCommand RefreshVersionsCommand { get; }
    public AsyncRelayCommand RefreshInstalledVersionsCommand { get; }
    public AsyncRelayCommand RefreshProfilesCommand { get; }
    public AsyncRelayCommand RefreshServersCommand { get; }
    public AsyncRelayCommand RefreshNewsCommand { get; }
    public AsyncRelayCommand RefreshInstancesCommand { get; }
    public AsyncRelayCommand DeleteInstanceCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand SignInMicrosoftCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }

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
        await RefreshInstalledVersionsAsync();
        await RefreshProfilesAsync();
        await RefreshServersAsync();
        await RefreshNewsAsync();
        await RefreshVersionsAsync();
    }

    private async Task RefreshInstancesAsync()
    {
        IsBusy = true;
        try
        {
            Append("Loading instances ...");
            var list = await _service.ListInstancesAsync(CancellationToken.None);

            Instances.Clear();
            foreach (var i in list) Instances.Add(i);

            Append($"Loaded {list.Count} instances.");
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
            if (SelectedInstance is { } inst)
            {
                var bumped = inst with { LastPlayedAt = DateTimeOffset.UtcNow };
                try
                {
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
