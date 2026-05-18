using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Modal that lets the user pick a new <see cref="Instance.IconKey"/> for an existing
/// instance. Reuses the same 15-tile picker layout as <see cref="NewInstanceDialog"/>
/// so the two flows stay visually consistent. Caller reads <see cref="Confirmed"/> and
/// <see cref="SelectedIconKey"/> after <c>ShowDialog</c> returns.
/// </summary>
public partial class EditInstanceIconDialog : Window
{
    /// <summary>The icon key the user picked. Initialized to the instance's current key.</summary>
    public string SelectedIconKey { get; private set; } = InstanceIcons.GrassBlock;

    /// <summary>True only when the user clicked Save (vs Cancel / close).</summary>
    public bool Confirmed { get; private set; }

    public EditInstanceIconDialog()
    {
        InitializeComponent();
        IconPicker.ItemsSource = InstanceIcons.All;
    }

    /// <summary>
    /// Factory: pre-populate the title, the subtitle ("Pick a new icon for &lt;name&gt;.")
    /// and pre-select the radio that matches the instance's current icon so the dialog
    /// opens already showing the user where they are.
    /// </summary>
    public static EditInstanceIconDialog ForInstance(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var dlg = new EditInstanceIconDialog
        {
            SelectedIconKey = instance.IconKey,
            Title = $"Change icon - {instance.Name}",
        };
        dlg.SubtitleText.Text = $"Pick a new icon for '{instance.Name}'.";
        // Pre-check the matching radio after the items have been generated.
        dlg.Opened += (_, _) => dlg.PreselectIcon(instance.IconKey);
        return dlg;
    }

    private void PreselectIcon(string iconKey)
    {
        // ItemsControl realizes RadioButtons lazily; iterate the visual tree once the
        // window is open and tick the one whose Tag matches the current icon key.
        foreach (var rb in IconPicker.GetVisualDescendants().OfType<RadioButton>())
        {
            if (rb.Tag is string key && string.Equals(key, iconKey, StringComparison.Ordinal))
            {
                rb.IsChecked = true;
                break;
            }
        }
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

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
