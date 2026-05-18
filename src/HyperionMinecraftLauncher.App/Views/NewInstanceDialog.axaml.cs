using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
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

    /// <summary>Mod loader the user picked; defaults to vanilla.</summary>
    public ModLoader SelectedLoader { get; private set; } = ModLoader.None;

    /// <summary>
    /// Loader version the user picked. <c>null</c> when (a) the loader is Vanilla, or (b) the
    /// user left the ComboBox empty -- in which case the installer picks the newest stable
    /// build at install time.
    /// </summary>
    public string? SelectedLoaderVersion =>
        LoaderVersionBox.SelectedItem as string;

    /// <summary>Set to true only when the user clicked Create with a valid version.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Last in-flight loader-versions query token. We cancel any pending request before
    /// kicking off a new one so a fast click between Forge -> Fabric doesn't race two
    /// network calls and apply the older one second.
    /// </summary>
    private CancellationTokenSource? _loaderVersionCts;

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

    private async void OnLoaderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string name }) return;
        if (!Enum.TryParse<ModLoader>(name, ignoreCase: true, out var loader)) return;
        SelectedLoader = loader;

        // Vanilla -> nothing to install, hide the picker entirely.
        if (loader == ModLoader.None)
        {
            LoaderVersionPanel.IsVisible = false;
            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.SelectedItem = null;
            return;
        }

        LoaderVersionPanel.IsVisible = true;
        LoaderVersionBox.ItemsSource = null;
        LoaderVersionBox.SelectedItem = null;
        LoaderVersionHint.Text = $"Loading {loader} versions ...";

        // Cancel any prior in-flight request.
        var prevCts = _loaderVersionCts;
        _loaderVersionCts = new CancellationTokenSource();
        prevCts?.Cancel();
        prevCts?.Dispose();
        var ct = _loaderVersionCts.Token;

        if (DataContext is not MainViewModel vm || SelectedVersion is not { } version)
        {
            LoaderVersionHint.Text = "Pick a Minecraft version first.";
            return;
        }

        try
        {
            var versions = await vm.ListLoaderVersionsAsync(loader, version.Name, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            if (versions.Count == 0)
            {
                LoaderVersionBox.ItemsSource = Array.Empty<string>();
                LoaderVersionHint.Text = $"No {loader} versions found for {version.Name} (will install latest).";
                return;
            }

            LoaderVersionBox.ItemsSource = versions;
            // Default to newest (first entry in the list per IModLoaderVersionFetcher contract).
            LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionHint.Text = $"{versions.Count} {loader} build(s) available. Newest selected.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer click; the next click already cleared the hint.
        }
        catch (Exception ex)
        {
            LoaderVersionHint.Text = $"Could not load {loader} versions: {ex.Message}";
        }
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
