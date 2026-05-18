using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

public partial class NewInstanceDialog : Window
{
    public string SelectedName => NameBox.Text?.Trim() ?? "Untitled";
    /// <summary>The version selected in the dialog. Reads from the bound MainViewModel when present
    /// so the Home-page filter chips drive the same selection here.</summary>
    public VersionMetadata? SelectedVersion =>
        (DataContext as MainViewModel)?.SelectedVersion
        ?? VersionBox.SelectedItem as VersionMetadata;
    public string SelectedIconKey { get; private set; } = InstanceIcons.GrassBlock;

    /// <summary>Set to true only when the user clicked Create with a valid version.</summary>
    public bool Confirmed { get; private set; }

    public NewInstanceDialog()
    {
        InitializeComponent();
        IconPicker.ItemsSource = InstanceIcons.All;
    }

    /// <summary>
    /// Bind the dialog to the live <see cref="MainViewModel"/> so the version dropdown,
    /// search box and chip toggles share state with the Home page picker. The dialog
    /// reads <c>FilteredVersions</c> (not <c>AvailableVersions</c>) so the user sees the
    /// same narrowed list as before clicking "+ New Instance".
    /// </summary>
    public static NewInstanceDialog WithViewModel(MainViewModel viewModel, string suggestedName = "My new instance")
    {
        var dlg = new NewInstanceDialog
        {
            DataContext = viewModel,
        };
        dlg.NameBox.Text = suggestedName;
        // Force a filter pass in case the source list changed since the user last
        // interacted with the picker; this also nudges SelectedVersion to the first
        // surviving entry when the previous one no longer matches.
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

    private void OnIconClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key })
            SelectedIconKey = key;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (SelectedVersion is null) return;          // version is the only hard requirement
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        Confirmed = true;
        Close();
    }
}

/// <summary>
/// Converts an icon-key string (one of <see cref="InstanceIcons"/>'s constants) into an
/// Avalonia <see cref="Bitmap"/> loaded from the App's bundled MC asset folder. Used by
/// the new-instance dialog's icon picker.
/// </summary>
public sealed class IconKeyToUri : IValueConverter
{
    public static readonly IconKeyToUri Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key)) return null;
        try
        {
            var uri = new Uri($"avares://HyperionMinecraftLauncher/Assets/Icons/MC/{key}.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
