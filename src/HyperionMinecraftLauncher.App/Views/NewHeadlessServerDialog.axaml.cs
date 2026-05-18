using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Modal that collects the four inputs the New Headless Server flow needs
/// (name, MC version, max heap in MiB, TCP port) and exposes them to the caller
/// after <c>ShowDialog</c> returns. The view-model bound to the dialog supplies
/// the version dropdown contents (<c>FilteredVersions</c> from MainViewModel).
/// </summary>
public partial class NewHeadlessServerDialog : Window
{
    /// <summary>The trimmed server name typed by the user. Empty string when nothing was typed.</summary>
    public string SelectedName => NameBox.Text?.Trim() ?? string.Empty;

    /// <summary>The Minecraft version highlighted in the dropdown.</summary>
    public VersionMetadata? SelectedVersion =>
        (DataContext as MainViewModel)?.SelectedVersion
        ?? VersionBox.SelectedItem as VersionMetadata;

    /// <summary>JVM max heap MiB selected on the slider.</summary>
    public int SelectedRamMb => (int)RamSlider.Value;

    /// <summary>TCP port typed into the spinner.</summary>
    public int SelectedPort => PortBox.Value is { } v ? (int)v : 25565;

    /// <summary>Set to true only when the user clicked Create with a valid version + name.</summary>
    public bool Confirmed { get; private set; }

    public NewHeadlessServerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Bind the dialog to the live <see cref="MainViewModel"/> so the version dropdown shares
    /// state with the Home page filter chips. Falls back to the first filtered version when
    /// nothing is currently selected so the Create button has something to commit.
    /// </summary>
    public static NewHeadlessServerDialog WithViewModel(MainViewModel viewModel, string suggestedName = "My dedicated server")
    {
        var dlg = new NewHeadlessServerDialog
        {
            DataContext = viewModel,
        };
        dlg.NameBox.Text = suggestedName;
        viewModel.ApplyVersionFilter();
        if (viewModel.SelectedVersion is null)
            viewModel.SelectedVersion = viewModel.FilteredVersions.FirstOrDefault();
        return dlg;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnRamSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // Live numeric label so the slider isn't an opaque blob.
        if (RamValueLabel is not null)
            RamValueLabel.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (SelectedVersion is null) return;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        Confirmed = true;
        Close();
    }
}
