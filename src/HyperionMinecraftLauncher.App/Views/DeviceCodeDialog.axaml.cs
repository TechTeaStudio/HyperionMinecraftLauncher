using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Modal that shows the Microsoft device-code prompt in a big-and-readable way:
/// huge bold code + the verification URL + Copy / Open-browser buttons. Auto-closes
/// when the parent <see cref="MainWindow"/> tells us auth succeeded.
/// </summary>
public partial class DeviceCodeDialog : Window
{
    private string _code = string.Empty;
    private string _url = string.Empty;

    public DeviceCodeDialog()
    {
        InitializeComponent();
    }

    /// <summary>Build a dialog pre-filled with the device-code info.</summary>
    public static DeviceCodeDialog ForCode(MicrosoftDeviceCodeInfo info)
    {
        var d = new DeviceCodeDialog
        {
            _code = info.UserCode,
            _url = info.VerificationUrl,
        };
        d.CodeText.Text = info.UserCode;
        d.UrlText.Text = info.VerificationUrl;
        return d;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && !string.IsNullOrEmpty(_code))
            await clipboard.SetTextAsync(_code);
    }

    private void OnOpenBrowser(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_url)) return;
        try { Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); } catch { }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
