using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Wraps a <see cref="ServerListEntry"/> with the most recent <see cref="ServerStatus"/> ping result
/// and exposes UI-friendly projections (status line text, traffic-light colour, etc.). The MainViewModel
/// owns one of these per row in the Servers page; <see cref="ApplyStatus"/> mutates state in place so
/// the existing ListBox row keeps its identity (no full collection rebuild required).
/// </summary>
public sealed class ServerListItemViewModel : INotifyPropertyChanged
{
    private ServerStatus? _status;
    private bool _isPinging;

    /// <summary>Construct from the underlying <c>servers.dat</c> entry.</summary>
    public ServerListItemViewModel(ServerListEntry entry)
    {
        Entry = entry;
    }

    /// <summary>Underlying NBT row (read-only after construction).</summary>
    public ServerListEntry Entry { get; }

    /// <summary>Display name from <c>servers.dat</c>.</summary>
    public string Name => Entry.Name;

    /// <summary>Hostname or <c>host:port</c> as the user wrote it.</summary>
    public string Ip => Entry.Ip;

    /// <summary>Most recent ping result; <c>null</c> means "never pinged" or "unreachable".</summary>
    public ServerStatus? Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLine));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    /// <summary>True while a ping is in flight (pings can take up to 3s each).</summary>
    public bool IsPinging
    {
        get => _isPinging;
        set
        {
            if (SetField(ref _isPinging, value))
            {
                OnPropertyChanged(nameof(StatusLine));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    /// <summary>True when we have a parseable status response.</summary>
    public bool HasStatus => _status is not null;

    /// <summary>
    /// Status text shown next to the server name. "Pinging ..." while a ping is in flight,
    /// "Offline" when the last ping failed, otherwise <c>{online}/{max} - {ms}ms</c>.
    /// </summary>
    public string StatusLine
    {
        get
        {
            if (_isPinging) return "Pinging ...";
            if (_status is null) return "Offline";
            return string.Create(CultureInfo.InvariantCulture, $"{_status.OnlinePlayers}/{_status.MaxPlayers} - {_status.LatencyMs}ms");
        }
    }

    /// <summary>
    /// Traffic-light colour: green &lt;100ms, yellow &lt;300ms, red &gt;=300ms,
    /// gray for "unreachable" / "never pinged".
    /// </summary>
    public IBrush StatusColor
    {
        get
        {
            if (_isPinging) return Brushes.LightSteelBlue;
            if (_status is null) return Brushes.Gray;
            var latency = _status.LatencyMs;
            if (latency < 100) return Brushes.LimeGreen;
            if (latency < 300) return Brushes.Gold;
            return Brushes.OrangeRed;
        }
    }

    /// <summary>Replace the cached status (called from the parent VM once a ping completes).</summary>
    public void ApplyStatus(ServerStatus? status)
    {
        Status = status;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

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
