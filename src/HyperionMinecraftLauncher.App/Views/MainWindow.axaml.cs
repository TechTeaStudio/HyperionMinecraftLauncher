using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MinecraftSkinRender.Image;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

public partial class MainWindow : Window
{
    private DeviceCodeDialog? _deviceCodeDialog;

    public MainWindow()
    {
        InitializeComponent();
        LoadDefaultSkin();
        Opened += (_, _) => HookViewModel();
    }

    private void HookViewModel()
    {
        if (DataContext is not MainViewModel vm) return;
        vm.DeviceCodeRequested += OnVmDeviceCodeRequested;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmDeviceCodeRequested(object? sender, MicrosoftDeviceCodeInfo info)
    {
        // Close any stale prompt (e.g. user re-clicked Sign-in mid-flow).
        _deviceCodeDialog?.Close();
        _deviceCodeDialog = DeviceCodeDialog.ForCode(info);
        // Fire-and-forget ShowDialog so the auth task keeps polling Microsoft.
        _ = _deviceCodeDialog.ShowDialog(this);
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.HasSession)
            || sender is not MainViewModel vm
            || !vm.HasSession)
            return;

        // Retire the device-code modal (auth completed).
        if (_deviceCodeDialog is { } d)
        {
            d.Close();
            _deviceCodeDialog = null;
        }

        // If the session is online (Microsoft), fetch the user's real skin and push it
        // into the 3D viewer so the Skins page shows their face instead of default Steve.
        if (vm.IsSignedInOnline && vm.CurrentSession is { Uuid: { Length: > 0 } uuid })
        {
            await TryReplaceSkinFromMojangAsync(uuid).ConfigureAwait(false);
        }
    }

    private async Task TryReplaceSkinFromMojangAsync(string uuid)
    {
        try
        {
            // Share the on-disk cache root with the news client so repeated sign-ins are instant
            // and offline launches still show the user's last-seen skin.
            var fetcher = new MojangPlayerSkinFetcher(
                new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) },
                new FileCache());
            var info = await fetcher.FetchAsync(uuid, CancellationToken.None).ConfigureAwait(false);
            if (info is null || info.SkinPng.Length == 0) return;

            // Marshal back to the UI thread before touching Avalonia controls.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplySkinBytes(info.SkinPng);
                if (info.CapePng is { Length: > 0 } capeBytes)
                {
                    try
                    {
                        using var capeMs = new MemoryStream(capeBytes);
                        SkinViewer.CapeSource = new Bitmap(capeMs);
                    }
                    catch { /* malformed cape PNG - skip */ }
                }
                else
                {
                    SkinViewer.CapeSource = null;
                }
            });
        }
        catch
        {
            // Quietly keep the bundled Steve skin - the user's auth + launch still work.
        }
    }

    private void LoadDefaultSkin()
    {
        // The bundled Steve PNG drives the Skins page and the account-chip avatar until
        // Microsoft sign-in fetches the user's own skin.
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://HyperionMinecraftLauncher/Assets/Icons/MC/steve.png"));
            var memoryBytes = new MemoryStream();
            stream.CopyTo(memoryBytes);
            ApplySkinBytes(memoryBytes.ToArray());
        }
        catch
        {
            // Asset loader failure - SkinViewer + avatar stay empty.
        }
    }

    /// <summary>Push a raw skin PNG into the Skins page preview AND into the account-chip avatar.</summary>
    private void ApplySkinBytes(byte[] png)
    {
        if (png.Length == 0) return;
        try
        {
            // Feed the full skin to the Skins-page preview.
            using (var ms = new MemoryStream(png))
                SkinViewer.SkinSource = new Bitmap(ms);

            // Build the chip avatar: crop the 8x8 face from the skin, then re-encode as PNG for
            // an Avalonia Bitmap. `Skin2DHeadTypeA.MakeHeadImage` returns the 2-layer-merged head,
            // which is exactly what every other launcher shows in the corner.
            using var sk = SKBitmap.Decode(png);
            if (sk is null) return;
            using var head = Skin2DHeadTypeA.MakeHeadImage(sk);
            using var data = head.Encode(SKEncodedImageFormat.Png, 100);
            using var headStream = data.AsStream();
            var avatar = new Bitmap(headStream);

            if (DataContext is MainViewModel vm)
                vm.AvatarBitmap = avatar;
        }
        catch
        {
            // Bad skin bytes - SkinViewer stays on the previous image, avatar untouched.
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
    private void OnNavLogs(object? sender, RoutedEventArgs e) => SetSection(NavSection.Logs);

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

    private async void OnNewInstanceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Need at least one version to choose from. If the manifest hasn't loaded yet,
        // trigger the refresh first so the picker isn't empty.
        if (vm.AvailableVersions.Count == 0)
            await vm.RefreshVersionsCommand.ExecuteAsync();

        var dialog = NewInstanceDialog.WithVersions(vm.AvailableVersions);
        await dialog.ShowDialog(this);

        if (dialog.Confirmed && dialog.SelectedVersion is { } v)
        {
            await vm.CreateInstanceAsync(dialog.SelectedName, v.Name, dialog.SelectedIconKey);
        }
    }

    /// <summary>
    /// Opens the icon-picker for a single instance. Wired up by both the tile's
    /// context-menu "Change icon..." entry and the small "..." overflow button.
    /// Sender's <c>Tag</c> carries the bound <see cref="TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance"/>.
    /// </summary>
    private async void OnChangeIconMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { Tag: TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Instance instance })
            return;

        // Defensive: the XAML already hides / disables the entry point for auto-imported
        // entries. Keep the runtime guard so any future surface (e.g. keyboard shortcut)
        // can't bypass it.
        if (instance.IsAutoImported) return;

        var dialog = EditInstanceIconDialog.ForInstance(instance);
        await dialog.ShowDialog(this);

        if (dialog.Confirmed)
        {
            await vm.ChangeInstanceIconAsync(instance, dialog.SelectedIconKey);
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

    // Opens the launcher's log directory in the platform's file manager.
    // Wrapped in try/catch because a missing shell handler must never crash the launcher.
    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dir = DefaultLogDirectory.Resolve();
            Directory.CreateDirectory(dir);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = false });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", dir) { UseShellExecute = false });
            }
            else
            {
                // Linux + other Unixes - xdg-open is the de-facto standard.
                Process.Start(new ProcessStartInfo("xdg-open", dir) { UseShellExecute = false });
            }
        }
        catch
        {
            // Best-effort: silently skip if the shell isn't available.
        }
    }

    // Copies the loaded log file (unfiltered) to the system clipboard.
    private async void OnCopyLogAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(vm.LogFileText ?? string.Empty);
        }
        catch
        {
            // Clipboard providers can be flaky on Linux without a session - swallow and move on.
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
