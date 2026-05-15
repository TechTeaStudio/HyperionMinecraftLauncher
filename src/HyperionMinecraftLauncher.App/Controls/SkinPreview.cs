using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MinecraftSkinRender.Image;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Lightweight skin preview: renders the player as a 3D-perspective head plus a 2D
/// full-body sprite, side by side. No OpenGL - it round-trips through Coloryr's
/// <c>MinecraftSkinRender.Image</c> NuGet (pure SkiaSharp). Pixels stay crisp.
/// </summary>
public sealed class SkinPreview : UserControl
{
    public static readonly StyledProperty<Bitmap?> SkinSourceProperty =
        AvaloniaProperty.Register<SkinPreview, Bitmap?>(nameof(SkinSource));

    public Bitmap? SkinSource
    {
        get => GetValue(SkinSourceProperty);
        set => SetValue(SkinSourceProperty, value);
    }

    private readonly Image _headImage = new()
    {
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8),
    };

    private readonly Image _bodyImage = new()
    {
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8),
    };

    static SkinPreview()
    {
        SkinSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.Rebuild());
    }

    public SkinPreview()
    {
        // Keep pixels chunky - the renderer outputs at native skin resolution, and we
        // scale up in the Image control via nearest-neighbour.
        RenderOptions.SetBitmapInterpolationMode(_headImage, BitmapInterpolationMode.None);
        RenderOptions.SetBitmapInterpolationMode(_bodyImage, BitmapInterpolationMode.None);

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
        };
        Grid.SetColumn(_headImage, 0);
        Grid.SetColumn(_bodyImage, 1);
        grid.Children.Add(_headImage);
        grid.Children.Add(_bodyImage);
        Content = grid;
    }

    private void Rebuild()
    {
        _headImage.Source = null;
        _bodyImage.Source = null;

        if (SkinSource is null) return;

        // Round-trip Avalonia Bitmap -> SKBitmap. The skin file is tiny (a few KB)
        // so PNG re-encode cost is negligible.
        SKBitmap? sk = null;
        try
        {
            using var ms = new MemoryStream();
            SkinSource.Save(ms);
            ms.Position = 0;
            sk = SKBitmap.Decode(ms);
            if (sk is null) return;

            // 3D-perspective head image (top + front + right face composite).
            _headImage.Source = ToAvaloniaBitmap(Skin3DHeadTypeB.MakeHeadImage(sk, 15, 65));

            // Full-body 2D sprite. Pass null for "auto-detect classic vs slim".
            using var body = Skin2DTypeA.MakeSkinImage(sk, null);
            _bodyImage.Source = ToAvaloniaBitmap(body);
        }
        catch
        {
            // Bad skin data - leave both images blank.
        }
        finally
        {
            sk?.Dispose();
        }
    }

    private static Bitmap? ToAvaloniaBitmap(SKImage? image)
    {
        if (image is null) return null;
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }

    private static Bitmap? ToAvaloniaBitmap(SKBitmap? bitmap)
    {
        if (bitmap is null) return null;
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
