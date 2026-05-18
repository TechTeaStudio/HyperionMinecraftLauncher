using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Tiny modal asking the user whether the PNG they picked is Classic (4-pixel arms,
/// Steve model) or Slim (3-pixel arms, Alex model). Returned via <see cref="SelectedVariant"/>;
/// <c>null</c> means the user cancelled.
/// </summary>
public partial class SkinVariantDialog : Window
{
    /// <summary>The variant the user chose, or <c>null</c> on cancel.</summary>
    public SkinVariant? SelectedVariant { get; private set; }

    public SkinVariantDialog()
    {
        InitializeComponent();
    }

    private void OnClassic(object? sender, RoutedEventArgs e)
    {
        SelectedVariant = SkinVariant.Classic;
        Close();
    }

    private void OnSlim(object? sender, RoutedEventArgs e)
    {
        SelectedVariant = SkinVariant.Slim;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        SelectedVariant = null;
        Close();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
