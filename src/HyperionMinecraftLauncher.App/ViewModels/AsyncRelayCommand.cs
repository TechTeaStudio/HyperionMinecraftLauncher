using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Minimal async <see cref="ICommand"/> implementation. No external MVVM toolkit dependency.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Construct with the action to run on Execute and an optional can-execute predicate.</summary>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (_isRunning) return false;
        return _canExecute?.Invoke() ?? true;
    }

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        await ExecuteAsync().ConfigureAwait(false);
    }

    /// <summary>Awaitable form, useful for tests.</summary>
    public async Task ExecuteAsync()
    {
        if (_isRunning) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(false);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Notify the UI that the result of <see cref="CanExecute"/> may have changed.</summary>
    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
