using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// View-model behind <c>MainWindow.axaml</c>. Owns username, version selection, log text,
/// and the two commands the window exposes (<see cref="RefreshVersionsCommand"/> and
/// <see cref="LaunchCommand"/>).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IMinecraftLauncherService _service;
    private readonly ILauncherLogger _logger;

    private string _username = "Steve";
    private VersionMetadata? _selectedVersion;
    private string _logText = string.Empty;
    private bool _isBusy;

    /// <summary>Construct the view-model with the launcher service it should drive and the log sink it should mirror to.</summary>
    public MainViewModel(IMinecraftLauncherService service, ILauncherLogger logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        AvailableVersions = new ObservableCollection<VersionMetadata>();
        RefreshVersionsCommand = new AsyncRelayCommand(RefreshVersionsAsync, () => !IsBusy);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsBusy && SelectedVersion is not null);
    }

    /// <summary>The username the user typed.</summary>
    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    /// <summary>Sorted list of versions returned by the service.</summary>
    public ObservableCollection<VersionMetadata> AvailableVersions { get; }

    /// <summary>Currently-selected version (drives <see cref="LaunchCommand"/>'s CanExecute).</summary>
    public VersionMetadata? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetField(ref _selectedVersion, value))
                LaunchCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Multi-line log displayed in the window's textbox.</summary>
    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    /// <summary>True while an async command is running. Disables both buttons.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshVersionsCommand.RaiseCanExecuteChanged();
                LaunchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Fetch the version manifest.</summary>
    public AsyncRelayCommand RefreshVersionsCommand { get; }

    /// <summary>Authenticate + install + launch the selected version.</summary>
    public AsyncRelayCommand LaunchCommand { get; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

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
            // The service already logged the underlying exception; record the user-facing surfacing here.
            _logger.Warn($"RefreshVersions surfaced LauncherException to UI: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LaunchAsync()
    {
        if (SelectedVersion is null) return;

        IsBusy = true;
        try
        {
            Append($"Authenticating '{Username}' (offline mode) ...");
            var auth = await _service.AuthenticateAsync(
                new AuthRequest { Mode = AuthMode.Offline, Username = Username },
                CancellationToken.None).ConfigureAwait(false);

            // We intentionally use a synchronous progress wrapper rather than System.Progress<T>:
            // Progress<T> posts to the captured SynchronizationContext, which races with our final
            // "Launched." Append on test threads (and the UI thread marshalling is handled by the
            // Avalonia binding system anyway when LogText raises PropertyChanged).
            var progress = new SynchronousProgress<LaunchProgress>(p =>
            {
                var fractionText = p.Fraction is double frac
                    ? $" {(frac * 100).ToString("0", CultureInfo.InvariantCulture)}%"
                    : string.Empty;
                var itemText = string.IsNullOrEmpty(p.CurrentItem) ? string.Empty : $" - {p.CurrentItem}";
                Append($"{p.Stage}{fractionText}{itemText}");
            });

            Append($"Launching {SelectedVersion.Name} ...");
            var result = await _service.LaunchAsync(
                new LaunchRequest { VersionName = SelectedVersion.Name, Session = auth },
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
