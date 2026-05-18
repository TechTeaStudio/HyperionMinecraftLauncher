using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Modpack-import dialog: shows the picked archive path, lets the user override
/// the instance name (default = manifest name), and confirms before the importer
/// actually starts pulling files. The importer itself runs in the view-model on
/// dismissal; a separate progress overlay surfaces the import status.
/// </summary>
public partial class ImportModpackDialog : Window
{
    /// <summary>Absolute path to the picked archive (.mrpack / .zip).</summary>
    public string? ArchivePath { get; private set; }

    /// <summary>User-chosen instance name. Empty = inherit from manifest.</summary>
    public string TargetInstanceName => NameBox.Text?.Trim() ?? string.Empty;

    /// <summary>True only when the user clicked Import (vs Cancel / X / Esc).</summary>
    public bool Confirmed { get; private set; }

    public ImportModpackDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog pre-populated with the picked archive and the manifest's default name.</summary>
    public static ImportModpackDialog ForArchive(string archivePath, string? suggestedName)
    {
        var dlg = new ImportModpackDialog
        {
            ArchivePath = archivePath,
        };
        dlg.ArchivePathBox.Text = archivePath;
        dlg.NameBox.Text = suggestedName ?? string.Empty;
        return dlg;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnImport(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ArchivePath)) return;
        Confirmed = true;
        Close();
    }
}
