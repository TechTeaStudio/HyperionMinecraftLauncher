using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// First-run onboarding for the CurseForge integration. CurseForge requires every
/// developer to obtain their own API key from <c>console.curseforge.com</c> - Hyperion
/// never ships one. This three-step modal walks the user through opening the console
/// in a browser, copying the key, and pasting it back into the launcher. The dialog
/// returns the entered key via <see cref="ResultApiKey"/>; a cancel returns <c>null</c>.
/// </summary>
public partial class CurseForgeOnboardingDialog : Window, INotifyPropertyChanged
{
    private string _enteredApiKey = string.Empty;

    /// <summary>
    /// Two-way bound to the body's TextBox. Trim is deferred to <see cref="OnSave"/> so the
    /// raw text stays in sync with the user's input until they confirm.
    /// </summary>
    public string EnteredApiKey
    {
        get => _enteredApiKey;
        set
        {
            if (_enteredApiKey == value) return;
            _enteredApiKey = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The non-empty trimmed key the user confirmed, or <c>null</c> when they cancelled or
    /// closed the dialog without saving. Callers should fall back to "no change" semantics
    /// when this stays <c>null</c>.
    /// </summary>
    public string? ResultApiKey { get; private set; }

    public CurseForgeOnboardingDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Construct the dialog pre-populated with the user's current key (typed back into the
    /// "paste your key" textbox). Used by the "Change key" entry point so the user can see
    /// what's already stored before pasting a fresh value.
    /// </summary>
    public static CurseForgeOnboardingDialog WithExistingKey(string? existingKey)
    {
        var dialog = new CurseForgeOnboardingDialog();
        if (!string.IsNullOrEmpty(existingKey))
            dialog.EnteredApiKey = existingKey;
        return dialog;
    }

    private void OnOpenConsole(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://console.curseforge.com/?#/api-keys")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort: a missing default browser shouldn't crash the launcher.
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var trimmed = (_enteredApiKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            // No validation beyond non-empty per spec; treat blank Save as cancel so the
            // caller doesn't wipe an existing key with an empty string.
            ResultApiKey = null;
            Close();
            return;
        }
        ResultApiKey = trimmed;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        ResultApiKey = null;
        Close();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    /// <inheritdoc />
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
}
