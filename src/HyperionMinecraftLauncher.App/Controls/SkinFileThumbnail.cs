using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Front view of the player built from a skin PNG on disk, for the Recent skins tiles. A plain
/// <c>Image</c> can't take a file path string from a binding, so these tiles used to stay blank.
/// </summary>
public sealed class SkinFileThumbnail : Image
{
    /// <summary>Path of the skin PNG.</summary>
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<SkinFileThumbnail, string?>(nameof(FilePath));

    /// <inheritdoc cref="FilePathProperty"/>
    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Image);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty)
            _ = ShowAsync(change.GetNewValue<string?>());
    }

    private async Task ShowAsync(string? path)
    {
        Source = null;
        if (string.IsNullOrEmpty(path)) return;
        var figure = await Task.Run(() => Render(path));
        if (FilePath == path) Source = figure;
    }

    private static Bitmap? Render(string path)
    {
        try
        {
            return BrowsedSkinMinifigureCache.RenderBodyFromPng(File.ReadAllBytes(path));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
