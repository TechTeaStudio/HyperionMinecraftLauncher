using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Async <see cref="ICommand"/> that carries a strongly-typed parameter (the picked
/// <c>Account</c> on the header chip flyout, for example). Mirrors
/// <see cref="AsyncRelayCommand"/> but the execute lambda and can-execute predicate
/// receive the unboxed parameter.
/// </summary>
public sealed class AsyncParameterRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Construct with an executor + optional predicate.</summary>
    public AsyncParameterRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
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
        return _canExecute?.Invoke(Unbox(parameter)) ?? true;
    }

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        await ExecuteAsync(Unbox(parameter)).ConfigureAwait(false);
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

    /// <summary>Notify the UI that the result of <see cref="CanExecute"/> may have changed (UI-thread safe).</summary>
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

    private static T? Unbox(object? parameter) => parameter is T typed ? typed : default;
}
