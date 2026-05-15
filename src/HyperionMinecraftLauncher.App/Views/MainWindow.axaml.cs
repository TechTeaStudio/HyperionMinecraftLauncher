using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadDefaultSkin();
    }

    private void LoadDefaultSkin()
    {
        // Wire the bundled Steve PNG into the 3D viewer so the Skins page has something to draw
        // even when the user is offline / not signed in. Microsoft sign-in will later fetch the
        // user's real skin from sessionserver.mojang.com (planned in a follow-up).
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://HyperionMinecraftLauncher/Assets/Icons/MC/steve.png"));
            SkinViewer.Skin = new Bitmap(stream);
        }
        catch
        {
            // Asset loader failure - the viewer just shows an empty area.
        }
    }

    // Custom title-bar: pointer down anywhere on the header (except on a button) starts a drag.
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only the primary mouse button initiates window-move; let other buttons through.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Double-click toggles maximize / restore (Windows convention).
        if (e.ClickCount >= 2)
        {
            ToggleMaximize();
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnWindowMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnWindowMaximizeRestore(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnWindowClose(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Sidebar radio buttons call these instead of two-way binding so SelectedSection
    // is set even when the radio "Click" happens on an already-checked item (the
    // RadioButton.IsChecked-bound path swallows that case under compiled bindings).
    private void OnNavHome(object? sender, RoutedEventArgs e) => SetSection(NavSection.Home);
    private void OnNavInstallations(object? sender, RoutedEventArgs e) => SetSection(NavSection.Installations);
    private void OnNavSkins(object? sender, RoutedEventArgs e) => SetSection(NavSection.Skins);
    private void OnNavServers(object? sender, RoutedEventArgs e) => SetSection(NavSection.Servers);
    private void OnNavNews(object? sender, RoutedEventArgs e) => SetSection(NavSection.News);
    private void OnNavSettings(object? sender, RoutedEventArgs e) => SetSection(NavSection.Settings);

    private void SetSection(NavSection section)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedSection = section;
    }

    // News "Read more" -> open the article in the user's default browser. Tag holds the URL.
    private void OnOpenExternalLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort: a missing default browser shouldn't crash the launcher.
            }
        }
    }

    private async void OnBrowseGameDir(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var topLevel = GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Pick Minecraft game directory",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path is { } uri)
        {
            vm.GameDirectoryOverride = uri.LocalPath;
        }
    }

    private async void OnBrowseJavaExe(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var topLevel = GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Pick Java executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Executable")
                {
                    Patterns = new[] { "java.exe", "javaw.exe", "java", "*.exe" },
                },
            },
        });
        if (files.Count > 0 && files[0].Path is { } uri)
        {
            vm.JavaExecutableOverride = uri.LocalPath;
        }
    }
}
