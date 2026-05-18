using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Two-button picker the View shows when the user clicks "Import from MultiMC...". Lets
/// them pick either the Prism / MultiMC zip or the unzipped on-disk instance folder; the
/// caller drives the actual <c>StorageProvider</c> file/folder picker once the user picks
/// a mode here.
/// </summary>
public enum MultiMcImportPickResult
{
    /// <summary>Dialog was closed without picking a source.</summary>
    Cancelled,

    /// <summary>User wants to pick a <c>.zip</c> file with <see cref="Avalonia.Platform.Storage.IStorageProvider.OpenFilePickerAsync"/>.</summary>
    Zip,

    /// <summary>User wants to pick an unzipped folder with <see cref="Avalonia.Platform.Storage.IStorageProvider.OpenFolderPickerAsync"/>.</summary>
    Folder,
}

/// <summary>Tiny modal: "Pick a zip... or a folder."</summary>
public partial class MultiMcImportPickDialog : Window
{
    /// <summary>Which path the user wants to take; <see cref="MultiMcImportPickResult.Cancelled"/> when they dismissed.</summary>
    public MultiMcImportPickResult Result { get; private set; } = MultiMcImportPickResult.Cancelled;

    public MultiMcImportPickDialog()
    {
        InitializeComponent();
    }

    private void OnPickZip(object? sender, RoutedEventArgs e)
    {
        Result = MultiMcImportPickResult.Zip;
        Close();
    }

    private void OnPickFolder(object? sender, RoutedEventArgs e)
    {
        Result = MultiMcImportPickResult.Folder;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = MultiMcImportPickResult.Cancelled;
        Close();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
