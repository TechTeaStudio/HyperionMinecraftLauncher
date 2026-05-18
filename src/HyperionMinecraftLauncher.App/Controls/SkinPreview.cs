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
    /// <summary>
    /// Optional process-wide hook for "[Skin viewer]" log lines (texture-decode failures,
    /// rotation-render exceptions, body-view fallbacks). Wired in <c>App.axaml.cs</c> to the
    /// Serilog launcher logger; null in tests so the control stays standalone.
    /// </summary>
    public static Action<string, Exception?>? Logger { get; set; }

    private static void Log(string message, Exception? ex = null)
    {
        try { Logger?.Invoke("[Skin viewer] " + message, ex); }
        catch { /* never let a logger sink break the UI thread */ }
    }

    /// <summary>
    /// Preferred input: raw PNG bytes. Goes straight to <see cref="SKBitmap.Decode(byte[])"/>
    /// so the original texture is preserved byte-for-byte (no Avalonia <c>Bitmap.Save</c>
    /// re-encode round-trip, which can corrupt the semi-transparent hat / jacket overlay
    /// pixels via pre-multiplied-alpha rounding and produce mis-coloured cube faces).
    /// </summary>
    public static readonly StyledProperty<byte[]?> SkinPngSourceProperty =
        AvaloniaProperty.Register<SkinPreview, byte[]?>(nameof(SkinPngSource));

    public byte[]? SkinPngSource
    {
        get => GetValue(SkinPngSourceProperty);
        set => SetValue(SkinPngSourceProperty, value);
    }

    /// <summary>
    /// Legacy input: Avalonia <see cref="Bitmap"/>. Only used when
    /// <see cref="SkinPngSourceProperty"/> is null. The Bitmap is round-tripped through
    /// Skia's PNG encoder; that round-trip is lossy for skins with overlay transparency.
    /// </summary>
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
        Text = "Drag the head to rotate (body follows)",
        FontSize = 10,
        Opacity = 0.55,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private SKBitmap? _skSkin;
    private SKBitmap? _skCape;
    // Skin3DHeadTypeB.MakeHeadImage(skin, x, y) takes:
    //   x = rotation around X axis (pitch  - tilt forward / back)
    //   y = rotation around Y axis (yaw    - turn left / right)
    // Default face-on view: the camera looks straight at the head front so the user
    // immediately recognises their own face (eyes, mouth, hair). The previous default
    // (yaw=65) showed a 3/4 angle which obscured the face on stylised skins where the
    // side textures carry no detail - making the 3D head appear "broken" compared to
    // the 2D body sprite that always shows the face front-on. yaw=90 puts the head
    // texture's standard front-face UV ((8,8)-(16,16)) directly under the camera so
    // the 2D and 3D heads visually agree on first load. Drag-to-rotate still works,
    // so the user can still inspect the sides and back.
    private int _pitch = 15;
    private int _yaw = 90;
    private Point? _dragStart;

    static SkinPreview()
    {
        SkinPngSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnSkinChanged());
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

        // Prefer the raw-PNG path: no Avalonia <-> Skia re-encode, so semi-transparent
        // overlay pixels (hat, jacket, sleeve, pants) survive intact. Fall back to the
        // legacy Bitmap path only when the host hasn't supplied PNG bytes yet.
        var png = SkinPngSource;
        if (png is { Length: > 0 })
        {
            try
            {
                _skSkin = SKBitmap.Decode(png);
                Log($"SkinPngSource decoded: pngBytes={png.Length}, w={_skSkin?.Width}, h={_skSkin?.Height}, ct={_skSkin?.ColorType}, at={_skSkin?.AlphaType}.");
            }
            catch (Exception ex)
            {
                Log("SKBitmap.Decode(byte[]) failed for SkinPngSource; falling back to Bitmap path.", ex);
                _skSkin = null;
            }
        }

        if (_skSkin is null && SkinSource is { } bitmap)
        {
            try
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms);
                ms.Position = 0;
                _skSkin = SKBitmap.Decode(ms);
                if (_skSkin is null)
                    Log("SKBitmap.Decode(stream) returned null after Bitmap.Save round-trip; ignoring.");
                else
                    Log($"SkinSource decoded via Bitmap.Save: w={_skSkin.Width}, h={_skSkin.Height}, ct={_skSkin.ColorType}, at={_skSkin.AlphaType}.");
            }
            catch (Exception ex)
            {
                Log("Bitmap.Save / SKBitmap.Decode round-trip threw; ignoring this skin update.", ex);
                _skSkin = null;
            }
        }

        if (_skSkin is null)
        {
            _headImage.Source = null;
            _bodyImage.Source = null;
            _hasBodyImage = false;
            return;
        }

        // The body sprite caches the last-rendered face (front vs back) via _hasBodyImage
        // + _lastBodyWasBack so dragging within the front-facing arc doesn't repaint the
        // body. When the skin BYTES change, that cache becomes stale (it's a different
        // texture even if the yaw arc hasn't moved); reset the flag so RebuildBody below
        // forces a fresh paint instead of short-circuiting to the previous skin's body.
        _hasBodyImage = false;

        RebuildHead();
        RebuildBody();
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
            if (_skCape is null)
            {
                Log("Cape decode returned null; hiding cape slot.");
                return;
            }

            using var cape = Cape2DTypaA.MakeCapeImage(_skCape);
            _capeImage.Source = ToAvaloniaBitmap(cape);
            _capeImage.IsVisible = true;
        }
        catch (Exception ex)
        {
            Log("Cape render threw; hiding cape slot.", ex);
        }
    }

    private void RebuildHead()
    {
        if (_skSkin is null) return;
        try
        {
            // Argument order is critical: Skin3DHeadTypeB.MakeHeadImage(skin, x, y) treats
            // x as the X-axis rotation (pitch) and y as the Y-axis rotation (yaw). Passing
            // them swapped is what caused v0.32.1's "drag right flips the head upside down"
            // bug - horizontal drag updates yaw, which must land in the second slot.
            using var head = Skin3DHeadTypeB.MakeHeadImage(_skSkin, _pitch, _yaw);
            _headImage.Source = ToAvaloniaBitmap(head);
        }
        catch (Exception ex)
        {
            Log($"RebuildHead failed (pitch={_pitch}, yaw={_yaw}); keeping previous frame.", ex);
        }
    }

    private bool _lastBodyWasBack;
    private bool _hasBodyImage;

    private void RebuildBody()
    {
        if (_skSkin is null) return;
        try
        {
            // MinecraftSkinRender.Image 1.2.0 does NOT ship a 3D body renderer, only a
            // 3D HEAD renderer. To still give the body a sense of rotation, we flip
            // between a stock front-view sprite (Skin2DTypeA) and a back-view sprite we
            // compose locally with the SDK's ExtractSubset / Mix primitives. We pick
            // whichever face matches the current yaw, so dragging the head past the side
            // visibly flips the body too.
            bool showBack = SkinRotation.IsBackFacing(_yaw);
            // Only re-render the body image when the face actually changes, keeping
            // pointer-move cheap (the body sprite is much pricier than the 220x220 head).
            if (_hasBodyImage && showBack == _lastBodyWasBack) return;

            using var body = showBack
                ? BackBodyComposer.MakeBackImage(_skSkin)
                : Skin2DTypeA.MakeSkinImage(_skSkin, null);
            _bodyImage.Source = ToAvaloniaBitmap(body);
            _lastBodyWasBack = showBack;
            _hasBodyImage = true;
        }
        catch (Exception ex)
        {
            Log("Body render threw; body slot left at previous image.", ex);
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

        // Horizontal drag rotates around the Y axis (yaw - the head turns left / right);
        // vertical drag rotates around the X axis (pitch - the head tilts up / down).
        // RebuildHead passes them in the correct (x=pitch, y=yaw) order to MakeHeadImage.
        var step = SkinRotation.AccumulateStep(_pitch, _yaw, dx, dy);
        _pitch = step.Pitch;
        _yaw = step.Yaw;
        _dragStart = now;
        RebuildHead();
        // RebuildBody short-circuits when the yaw stays on the same side, so dragging
        // within the front-facing arc costs zero allocations for the body slot.
        RebuildBody();
    }

    private void OnHeadPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
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
