using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Minimal "Worlds for selected instance" picker. Reads top-level subdirectories of
/// <c>&lt;gameDir&gt;/saves/</c> directly - intentionally lightweight until T2's
/// <c>InstanceWorlds</c> tab lands and supersedes this dialog.
/// </summary>
public partial class WorldsDialog : Window
{
    /// <summary>The world folder the user picked, or <c>null</c> on cancel.</summary>
    public string? SelectedWorldFolder => Confirmed ? WorldsList.SelectedItem as string : null;

    /// <summary>True only when the user clicked Resume with a world selected.</summary>
    public bool Confirmed { get; private set; }

    public WorldsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Build a configured dialog for the given instance + override game dir. The dialog reads
    /// <c>&lt;gameDir&gt;/saves/</c>'s subfolders synchronously on construction - the folder
    /// listing is small (a few dozen entries on a typical install).
    /// </summary>
    public static WorldsDialog ForInstance(Instance instance, string? gameDirOverride)
    {
        var dlg = new WorldsDialog();
        var root = string.IsNullOrWhiteSpace(gameDirOverride)
            ? Core.Installations.DefaultMinecraftInstallationLocator.ResolveRoot()
            : gameDirOverride!;
        var savesDir = Path.Combine(root, "saves");

        dlg.HeaderText.Text = $"Resume a world for '{instance.Name}' ({instance.VersionId}).";
        dlg.SavesPathText.Text = savesDir;

        IReadOnlyList<string> worlds = TryListWorlds(savesDir);
        dlg.WorldsList.ItemsSource = worlds;
        dlg.WorldsList.SelectedItem = worlds.FirstOrDefault();
        return dlg;
    }

    private static IReadOnlyList<string> TryListWorlds(string savesDir)
    {
        try
        {
            if (!Directory.Exists(savesDir)) return Array.Empty<string>();
            return Directory.GetDirectories(savesDir)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (WorldsList.SelectedItem is not string) return;
        Confirmed = true;
        Close();
    }
}
