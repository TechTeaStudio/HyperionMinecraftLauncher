using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
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

    private string _username = "Steve";
    private VersionMetadata? _selectedVersion;
    private InstalledVersion? _selectedInstalledVersion;
    private LauncherProfile? _selectedProfile;
    private string _logText = string.Empty;
    private bool _isBusy;
    private AuthResult? _currentSession;
    private NavSection _selectedSection = NavSection.Home;

    /// <summary>Construct with the launcher service and (optional) Microsoft auth provider.</summary>
    public MainViewModel(IMinecraftLauncherService service, ILauncherLogger logger, IMicrosoftAuthService? microsoftAuth = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _microsoftAuth = microsoftAuth;

        AvailableVersions = new ObservableCollection<VersionMetadata>();
        InstalledVersions = new ObservableCollection<InstalledVersion>();
        Profiles = new ObservableCollection<LauncherProfile>();
        Servers = new ObservableCollection<ServerListEntry>();

        RefreshVersionsCommand = new AsyncRelayCommand(RefreshVersionsAsync, () => !IsBusy);
        RefreshInstalledVersionsCommand = new AsyncRelayCommand(RefreshInstalledVersionsAsync, () => !IsBusy);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync, () => !IsBusy);
        RefreshServersCommand = new AsyncRelayCommand(RefreshServersAsync, () => !IsBusy);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanLaunch);
        SignInMicrosoftCommand = new AsyncRelayCommand(SignInMicrosoftAsync, () => !IsBusy && !IsSignedInOnline);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, () => !IsBusy && IsSignedInOnline);
    }

    // ---- Account ----

    /// <summary>True when the user is signed in via Microsoft (not offline).</summary>
    public bool IsSignedInOnline => _currentSession is { IsOffline: false };

    /// <summary>True when there is any session (offline or Microsoft). Drives the account-chip label visibility.</summary>
    public bool HasSession => _currentSession is not null;

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
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand SignInMicrosoftCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool CanLaunch()
        => !IsBusy && (SelectedInstalledVersion is not null || SelectedVersion is not null);

    private async Task RefreshVersionsAsync()
    {
        IsBusy = true;
        try
        {
            Append("Loading version manifest ...");
            var versions = await _service.ListVersionsAsync(CancellationToken.None).ConfigureAwait(false);

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

    private async Task RefreshServersAsync()
    {
        IsBusy = true;
        try
        {
            Append("Reading servers.dat ...");
            var servers = await _service.ListServersAsync(CancellationToken.None).ConfigureAwait(false);

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
            var profiles = await _service.ListProfilesAsync(CancellationToken.None).ConfigureAwait(false);

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
            var versions = await _service.ListInstalledVersionsAsync(CancellationToken.None).ConfigureAwait(false);

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

    private async Task SignInMicrosoftAsync()
    {
        IsBusy = true;
        try
        {
            Append("Signing in with Microsoft ...");
            var auth = await _service.AuthenticateAsync(
                new AuthRequest { Mode = AuthMode.Microsoft, Username = string.Empty },
                CancellationToken.None).ConfigureAwait(false);

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
                await _microsoftAuth.SignOutAsync(CancellationToken.None).ConfigureAwait(false);
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
        // Choose the version to launch: prefer an installed one (no install step), fall back to manifest.
        var versionName = SelectedInstalledVersion?.Id ?? SelectedVersion?.Name;
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
                    CancellationToken.None).ConfigureAwait(false);
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

            Append($"Launching {versionName} ...");
            var result = await _service.LaunchAsync(
                new LaunchRequest { VersionName = versionName, Session = auth },
                progress,
                CancellationToken.None).ConfigureAwait(false);

            Append($"Launched. pid={result.ProcessId} version={result.VersionName}");
            _logger.Info($"UI: launch complete (pid {result.ProcessId}, version {result.VersionName}).");
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
