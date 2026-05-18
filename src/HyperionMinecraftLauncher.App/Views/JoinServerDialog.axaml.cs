using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Confirmation modal for the Servers page "Join" button. Shows the server name and
/// the resolved <c>host</c> string, plus an instance picker (defaults to the caller's
/// preselected instance) so the user can choose which instance to launch the join with.
/// </summary>
public partial class JoinServerDialog : Window
{
    /// <summary>The instance the user picked. <c>null</c> when the user cancelled.</summary>
    public Instance? SelectedInstance => Confirmed ? InstanceBox.SelectedItem as Instance : null;

    /// <summary>True only when the user clicked Join with an instance selected.</summary>
    public bool Confirmed { get; private set; }

    public JoinServerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Build a configured dialog for a given server entry, the available instances, and the
    /// preselected instance (typically <see cref="ViewModels.MainViewModel.SelectedInstance"/>).
    /// </summary>
    public static JoinServerDialog ForServer(
        ServerListEntry server,
        IEnumerable<Instance> instances,
        Instance? preselected)
    {
        var dlg = new JoinServerDialog();
        dlg.QuestionText.Text = $"Join '{server.Name}' on {server.Ip}?";
        var list = instances.ToList();
        dlg.InstanceBox.ItemsSource = list;
        dlg.InstanceBox.SelectedItem = preselected ?? list.FirstOrDefault();
        return dlg;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (InstanceBox.SelectedItem is not Instance) return;
        Confirmed = true;
        Close();
    }
}
