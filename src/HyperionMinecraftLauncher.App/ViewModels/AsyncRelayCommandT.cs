using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Parametrised async <see cref="ICommand"/>. The command parameter is forwarded to the
/// execute delegate; the can-execute predicate also receives the parameter so the View
/// can disable the per-item button based on the bound item.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand where T : class
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
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
        return _canExecute?.Invoke(parameter as T) ?? true;
    }

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter as T).ConfigureAwait(false);
    }

    /// <summary>Awaitable form, useful for tests.</summary>
    public async Task ExecuteAsync(T? parameter)
    {
        if (_isRunning) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter).ConfigureAwait(false);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Marshal a CanExecuteChanged notification to the UI thread.</summary>
    public void RaiseCanExecuteChanged()
    {
        var handler = CanExecuteChanged;
        if (handler is null) return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            handler(this, EventArgs.Empty);
            return;
        }
        Dispatcher.UIThread.Post(() => handler(this, EventArgs.Empty));
    }
}
