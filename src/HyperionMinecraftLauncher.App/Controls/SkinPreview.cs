using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MinecraftSkinRender.Image;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Skin preview: 3D-perspective head (drag-to-rotate) plus a 2D full-body sprite and,
/// when the player owns one, the cape. Backed by the pure-Skia <c>MinecraftSkinRender.Image</c>
/// pipeline - no GL context, no GL errors, fully interactive head rotation re-renders
/// the head image on each pointer move.
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

    public static readonly StyledProperty<Bitmap?> CapeSourceProperty =
        AvaloniaProperty.Register<SkinPreview, Bitmap?>(nameof(CapeSource));

    public Bitmap? CapeSource
    {
        get => GetValue(CapeSourceProperty);
        set => SetValue(CapeSourceProperty, value);
    }

    private readonly Image _headImage = new()
    {
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8),
        Cursor = new Cursor(StandardCursorType.SizeAll),
    };

    private readonly Image _bodyImage = new()
    {
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8),
    };

    private readonly Image _capeImage = new()
    {
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8),
        IsVisible = false,
    };

    private readonly TextBlock _hint = new()
    {
        Text = "Drag the head to rotate",
        FontSize = 10,
        Opacity = 0.55,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private SKBitmap? _skSkin;
    private SKBitmap? _skCape;
    private int _yaw = 15;    // Skin3DHeadTypeB params; default values chosen for a friendly 3/4 view.
    private int _pitch = 65;
    private Point? _dragStart;

    static SkinPreview()
    {
        SkinSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnSkinChanged());
        CapeSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnCapeChanged());
    }

    public SkinPreview()
    {
        RenderOptions.SetBitmapInterpolationMode(_headImage, BitmapInterpolationMode.None);
        RenderOptions.SetBitmapInterpolationMode(_bodyImage, BitmapInterpolationMode.None);
        RenderOptions.SetBitmapInterpolationMode(_capeImage, BitmapInterpolationMode.None);

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,Auto") };
        Grid.SetColumn(_headImage, 0);
        Grid.SetColumn(_bodyImage, 1);
        Grid.SetColumn(_capeImage, 2);
        _capeImage.Width = 72;     // cape is narrow; cap so it doesn't dominate the layout

        grid.Children.Add(_headImage);
        grid.Children.Add(_bodyImage);
        grid.Children.Add(_capeImage);

        var stack = new Panel();
        stack.Children.Add(grid);
        stack.Children.Add(_hint);
        Content = stack;

        // Mouse rotation lives on the head image. The head re-renders on each pointer
        // move, which is fast because Skin3DHeadTypeB operates on a 64x64 source.
        _headImage.PointerPressed += OnHeadPointerPressed;
        _headImage.PointerMoved += OnHeadPointerMoved;
        _headImage.PointerReleased += OnHeadPointerReleased;
    }

    private void OnSkinChanged()
    {
        _skSkin?.Dispose();
        _skSkin = null;

        if (SkinSource is null)
        {
            _headImage.Source = null;
            _bodyImage.Source = null;
            return;
        }

        try
        {
            using var ms = new MemoryStream();
            SkinSource.Save(ms);
            ms.Position = 0;
            _skSkin = SKBitmap.Decode(ms);
            if (_skSkin is null) return;

            RebuildHead();
            RebuildBody();
        }
        catch
        {
            // Bad skin - keep last image
        }
    }

    private void OnCapeChanged()
    {
        _skCape?.Dispose();
        _skCape = null;
        _capeImage.IsVisible = false;
        _capeImage.Source = null;

        if (CapeSource is null) return;

        try
        {
            using var ms = new MemoryStream();
            CapeSource.Save(ms);
            ms.Position = 0;
            _skCape = SKBitmap.Decode(ms);
            if (_skCape is null) return;

            using var cape = Cape2DTypaA.MakeCapeImage(_skCape);
            _capeImage.Source = ToAvaloniaBitmap(cape);
            _capeImage.IsVisible = true;
        }
        catch
        {
            // ignore
        }
    }

    private void RebuildHead()
    {
        if (_skSkin is null) return;
        try
        {
            using var head = Skin3DHeadTypeB.MakeHeadImage(_skSkin, _yaw, _pitch);
            _headImage.Source = ToAvaloniaBitmap(head);
        }
        catch
        {
            // ignore - keep previous head
        }
    }

    private void RebuildBody()
    {
        if (_skSkin is null) return;
        try
        {
            using var body = Skin2DTypeA.MakeSkinImage(_skSkin, null);
            _bodyImage.Source = ToAvaloniaBitmap(body);
        }
        catch
        {
            // ignore
        }
    }

    private void OnHeadPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(_headImage);
        e.Pointer.Capture(_headImage);
    }

    private void OnHeadPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var now = e.GetPosition(_headImage);
        var dx = (int)(now.X - start.X);
        var dy = (int)(now.Y - start.Y);
        if (dx == 0 && dy == 0) return;

        // Map mouse delta into the head renderer's (x, y) params. These tweak the camera
        // azimuth + elevation, so the live re-render rotates the head under the cursor.
        _yaw = WrapDeg(_yaw + dx);
        _pitch = Math.Clamp(_pitch - dy, 5, 175);
        _dragStart = now;
        RebuildHead();
    }

    private void OnHeadPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    private static int WrapDeg(int v)
    {
        v = ((v % 360) + 360) % 360;
        return v;
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
