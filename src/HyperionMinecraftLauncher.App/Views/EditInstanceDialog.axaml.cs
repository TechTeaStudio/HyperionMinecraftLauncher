using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Modal that lets the user edit an existing <see cref="Instance"/>: name, icon, and the
/// per-instance JVM / memory / game-dir / window-resolution overrides introduced in v0.30.
/// On <c>Save</c> the dialog populates <see cref="Result"/> with a new <see cref="Instance"/>
/// record (the input instance plus the edited fields). Cancel leaves <see cref="Confirmed"/>
/// false and the caller does nothing.
/// </summary>
/// <remarks>
/// Empty / blank text fields and zero numeric values are stored as <c>null</c> on the
/// instance, which means "inherit the global Settings value at launch time" via
/// <see cref="InstanceLaunchSettings.Merge"/>.
/// </remarks>
public partial class EditInstanceDialog : Window
{
    private Instance _original = null!;
    private string _selectedIconKey = InstanceIcons.GrassBlock;
    private int _maxAllowedMemoryMb = 8192;
    private bool _syncingSliders;

    /// <summary>The edited instance record, populated only when the user clicks Save.</summary>
    public Instance? Result { get; private set; }

    /// <summary>True only when the user clicked Save (vs Cancel / close).</summary>
    public bool Confirmed { get; private set; }

    public EditInstanceDialog()
    {
        InitializeComponent();
        IconPicker.ItemsSource = InstanceIcons.All;
    }

    /// <summary>
    /// Factory: pre-fill the dialog from <paramref name="instance"/>. The sliders' upper
    /// bound is taken from <see cref="SystemRam.RecommendedMaxHeapMb"/> so the user can't
    /// pick a value Hyperion wouldn't accept on the Settings page either.
    /// </summary>
    public static EditInstanceDialog ForInstance(Instance instance, LauncherSettings globalSettings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(globalSettings);

        var dlg = new EditInstanceDialog
        {
            _original = instance,
            _selectedIconKey = instance.IconKey,
            _maxAllowedMemoryMb = SystemRam.RecommendedMaxHeapMb(),
            Title = $"Edit instance - {instance.Name}",
        };
        dlg.TitleText.Text = $"Edit instance - {instance.Name}";

        dlg.NameBox.Text = instance.Name;
        dlg.MinRamSlider.Maximum = dlg._maxAllowedMemoryMb;
        dlg.MaxRamSlider.Maximum = dlg._maxAllowedMemoryMb;
        var defaultMin = instance.MinimumRamMb ?? globalSettings.MinimumRamMb;
        var defaultMax = instance.MaximumRamMb ?? globalSettings.MaximumRamMb;
        dlg.MinRamSlider.Value = Math.Clamp(defaultMin, 256, dlg._maxAllowedMemoryMb);
        dlg.MaxRamSlider.Value = Math.Clamp(defaultMax, 512, dlg._maxAllowedMemoryMb);
        dlg.MinRamLabel.Text = $"{(int)dlg.MinRamSlider.Value} MB";
        dlg.MaxRamLabel.Text = $"{(int)dlg.MaxRamSlider.Value} MB";

        dlg.JvmArgsBox.Text = instance.JvmArguments ?? string.Empty;
        dlg.GameDirBox.Text = instance.GameDirectory ?? string.Empty;
        dlg.WidthBox.Value = instance.ResolutionWidth ?? 0;
        dlg.HeightBox.Value = instance.ResolutionHeight ?? 0;

        dlg.Opened += (_, _) => dlg.PreselectIcon(instance.IconKey);
        return dlg;
    }

    private void PreselectIcon(string iconKey)
    {
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
            _selectedIconKey = key;
    }

    private void OnMinRamChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSliders) return;
        // Avalonia raises ValueChanged once during XAML init when the slider coerces its
        // default value into the Minimum..Maximum range (256..100 by default). At that
        // point the named labels further down the tree haven't been generated yet, so
        // a naive access NREs and crashes the dialog before it opens. Bail out until the
        // controls are wired up; the ForInstance factory sets the labels explicitly anyway.
        if (MinRamLabel is null || MinRamSlider is null || MaxRamSlider is null || MaxRamLabel is null) return;
        MinRamLabel.Text = $"{(int)MinRamSlider.Value} MB";
        // Keep the max >= min so the JVM doesn't reject Xmx < Xms.
        if (MaxRamSlider.Value < MinRamSlider.Value)
        {
            _syncingSliders = true;
            MaxRamSlider.Value = MinRamSlider.Value;
            MaxRamLabel.Text = $"{(int)MaxRamSlider.Value} MB";
            _syncingSliders = false;
        }
    }

    private void OnMaxRamChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSliders) return;
        // Same XAML-init race as OnMinRamChanged - see that handler's comment.
        if (MaxRamLabel is null || MinRamSlider is null || MaxRamSlider is null || MinRamLabel is null) return;
        MaxRamLabel.Text = $"{(int)MaxRamSlider.Value} MB";
        if (MaxRamSlider.Value < MinRamSlider.Value)
        {
            _syncingSliders = true;
            MinRamSlider.Value = MaxRamSlider.Value;
            MinRamLabel.Text = $"{(int)MinRamSlider.Value} MB";
            _syncingSliders = false;
        }
    }

    private async void OnBrowseGameDir(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Pick a game directory for this instance",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].Path is { } uri)
            GameDirBox.Text = uri.LocalPath;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Name is required; everything else has a "null = inherit" fallback.
        var trimmedName = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) return;

        var min = (int)MinRamSlider.Value;
        var max = (int)MaxRamSlider.Value;

        // Treat slider values that match the global default as "no override" so the
        // record stays slim and silent re-merges keep working when Settings change.
        int? minOverride = min;
        int? maxOverride = max;

        var jvm = JvmArgsBox.Text;
        string? jvmOverride = string.IsNullOrWhiteSpace(jvm) ? null : jvm;

        var dir = GameDirBox.Text;
        string? dirOverride = string.IsNullOrWhiteSpace(dir) ? null : dir;

        var widthValue = WidthBox.Value ?? 0;
        var heightValue = HeightBox.Value ?? 0;
        int? widthOverride = widthValue > 0 ? (int)widthValue : null;
        int? heightOverride = heightValue > 0 ? (int)heightValue : null;

        Result = _original with
        {
            Name = trimmedName,
            IconKey = _selectedIconKey,
            MinimumRamMb = minOverride,
            MaximumRamMb = maxOverride,
            JvmArguments = jvmOverride,
            GameDirectory = dirOverride,
            ResolutionWidth = widthOverride,
            ResolutionHeight = heightOverride,
        };
        Confirmed = true;
        Close();
    }
}
