using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.App.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Asks the player before a skin goes to Mojang, showing the new skin on the walking 3D model.
/// <see cref="Confirmed"/> is true only after "Change skin"; closing, Escape or Cancel leave it false.
/// </summary>
public partial class SkinChangeConfirmDialog : Window
{
    /// <summary>True when the player confirmed the change.</summary>
    public bool Confirmed { get; private set; }

    public SkinChangeConfirmDialog()
    {
        InitializeComponent();
    }

    /// <param name="request">The skin to confirm.</param>
    /// <param name="capePng">The cape the account wears, so the preview looks like the result.</param>
    public SkinChangeConfirmDialog(SkinChangeConfirmation request, byte[]? capePng) : this()
    {
        var slim = request.Variant == SkinVariant.Slim;
        Preview.IsSlim = slim;
        Preview.SkinPngSource = request.NewPngBytes;
        Preview.CapePngSource = capePng;
        var account = string.IsNullOrWhiteSpace(request.AccountName) ? "?" : request.AccountName;
        MessageText.Text = string.Format(Strings.SkinChangeDialog_Message, account);
        ModelText.Text = string.Format(Strings.SkinChangeDialog_Model,
            slim ? Strings.SkinVariantDialog_Slim : Strings.SkinVariantDialog_Classic);
        // Enter must not change a skin by accident: focus starts on Cancel.
        Opened += (_, _) => CancelButton.Focus();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
