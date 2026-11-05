using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MinecraftSkinRender;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.Localization;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Skins-page preview: the full 3D player model (both skin layers and the cape) walking in
/// place, drawn by the software <see cref="PlayerSkinRenderer"/> on a worker thread and shown
/// through a <see cref="WriteableBitmap"/> at device-pixel resolution. The UI thread only
/// starts a frame and copies the finished pixels, so the window stays responsive; a new frame
/// starts only after the previous one is on screen. Drag to orbit, double-click to reset the
/// view, the Walk toggle pauses the animation. Nothing renders while the control is hidden,
/// and a paused model re-renders only when something changes.
/// </summary>
public sealed class SkinPreview : UserControl
{
    private const float DefaultYaw = 25f;
    private const float DefaultPitch = 10f;
    private const int MaxFramePixels = 1400;
    // Above this many output pixels the frame is already smooth enough without 2x2 supersampling.
    private const int SupersampleBelowPixels = 200_000;

    /// <summary>
    /// Optional process-wide hook for "[Skin viewer]" log lines. Wired in <c>App.axaml.cs</c>
    /// to the Serilog launcher logger; null in tests so the control stays standalone.
    /// </summary>
    public static Action<string, Exception?>? Logger { get; set; }

    /// <summary>Skin as raw PNG bytes (64x64, legacy 64x32 or HD).</summary>
    public static readonly StyledProperty<byte[]?> SkinPngSourceProperty =
        AvaloniaProperty.Register<SkinPreview, byte[]?>(nameof(SkinPngSource));

    /// <summary>Legacy input used only when <see cref="SkinPngSource"/> is null.</summary>
    public static readonly StyledProperty<Bitmap?> SkinSourceProperty =
        AvaloniaProperty.Register<SkinPreview, Bitmap?>(nameof(SkinSource));

    /// <summary>Cape as raw PNG bytes, or null for no cape.</summary>
    public static readonly StyledProperty<byte[]?> CapePngSourceProperty =
        AvaloniaProperty.Register<SkinPreview, byte[]?>(nameof(CapePngSource));

    /// <summary>Slim (3 px) arms. Null detects the arm model from the texture.</summary>
    public static readonly StyledProperty<bool?> IsSlimProperty =
        AvaloniaProperty.Register<SkinPreview, bool?>(nameof(IsSlim));

    /// <summary>Play the walking animation.</summary>
    public static readonly StyledProperty<bool> IsWalkingProperty =
        AvaloniaProperty.Register<SkinPreview, bool>(nameof(IsWalking), defaultValue: true);

    public byte[]? SkinPngSource
    {
        get => GetValue(SkinPngSourceProperty);
        set => SetValue(SkinPngSourceProperty, value);
    }

    public Bitmap? SkinSource
    {
        get => GetValue(SkinSourceProperty);
        set => SetValue(SkinSourceProperty, value);
    }

    public byte[]? CapePngSource
    {
        get => GetValue(CapePngSourceProperty);
        set => SetValue(CapePngSourceProperty, value);
    }

    public bool? IsSlim
    {
        get => GetValue(IsSlimProperty);
        set => SetValue(IsSlimProperty, value);
    }

    public bool IsWalking
    {
        get => GetValue(IsWalkingProperty);
        set => SetValue(IsWalkingProperty, value);
    }

    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private PlayerSkinRenderer? _renderer;
    private SkinTexture? _skin;
    private CapeTexture? _cape;
    private WriteableBitmap? _frame;
    private byte[] _buffer = Array.Empty<byte>();
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private double _walkSeconds;
    private Task? _frameJob;
    private TimeSpan _lastTick;
    private Point? _dragFrom;
    private bool _dirty = true;

    static SkinPreview()
    {
        SkinPngSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnSkinChanged());
        SkinSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnSkinChanged());
        IsSlimProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnSkinChanged());
        CapePngSourceProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s.OnCapeChanged());
        IsWalkingProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s._dirty = true);
        BoundsProperty.Changed.AddClassHandler<SkinPreview>((s, _) => s._dirty = true);
    }

    public SkinPreview()
    {
        var walk = new ToggleButton
        {
            Content = Strings.Skins_WalkToggle,
            Classes = { "Chip" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            [!ToggleButton.IsCheckedProperty] = this[!IsWalkingProperty],
        };
        walk.IsCheckedChanged += (_, _) => IsWalking = walk.IsChecked == true;

        var hint = new TextBlock
        {
            Text = Strings.Skins_ViewerHint,
            FontSize = 13.5,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, 6),
            IsHitTestVisible = false,
        };

        var root = new Panel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll) };
        root.Children.Add(_image);
        root.Children.Add(hint);
        root.Children.Add(walk);
        Content = root;

        root.PointerPressed += OnPointerPressed;
        root.PointerMoved += OnPointerMoved;
        root.PointerReleased += OnPointerReleased;
        root.DoubleTapped += (_, _) => ResetView();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => OnTick());
    }

    private static void Log(string message, Exception? ex = null)
    {
        try { Logger?.Invoke("[Skin viewer] " + message, ex); }
        catch { /* never let a logger sink break the UI thread */ }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Start();
        _lastTick = _clock.Elapsed;
        _dirty = true;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick()
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Skip all work while the Skins page (or the window) is hidden.
        if (_renderer is null || !IsEffectivelyVisible || Bounds.Width < 1 || Bounds.Height < 1) return;

        if (IsWalking)
        {
            _walkSeconds += Math.Min(dt, 0.25);
            _dirty = true;
        }
        if (!_dirty || _frameJob is not null) return;
        _dirty = false;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Clamp((int)(Bounds.Width * scaling), 16, MaxFramePixels);
        var height = Math.Clamp((int)(Bounds.Height * scaling), 16, MaxFramePixels);
        if (_buffer.Length != width * height * 4)
            _buffer = new byte[width * height * 4];

        var renderer = _renderer;
        var buffer = _buffer;
        var pose = IsWalking
            ? PlayerPose.Walking(_yaw, _pitch, _walkSeconds)
            : new PlayerPose(_yaw, _pitch);
        renderer.Supersampling = width * height <= SupersampleBelowPixels ? 2 : 1;

        _frameJob = Task.Run(() => renderer.Render(pose, buffer, width, height));
        _frameJob.ContinueWith(
            job => Dispatcher.UIThread.Post(() => OnFrameRendered(job, renderer, buffer, width, height, scaling)),
            TaskScheduler.Default);
    }

    private void OnFrameRendered(Task job, PlayerSkinRenderer renderer, byte[] buffer, int width, int height, double scaling)
    {
        _frameJob = null;
        if (job.IsFaulted)
        {
            Log("Frame render failed; stopping the animation.", job.Exception?.GetBaseException());
            _timer.Stop();
            return;
        }

        // The skin changed while this frame was rendering: drop it and draw the new skin.
        if (!ReferenceEquals(renderer, _renderer))
        {
            _dirty = true;
            return;
        }

        if (_frame is null || _frame.PixelSize.Width != width || _frame.PixelSize.Height != height)
        {
            _frame?.Dispose();
            _frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            _image.Source = _frame;
        }

        using (var target = _frame.Lock())
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
                Marshal.Copy(buffer, y * rowBytes, target.Address + y * target.RowBytes, rowBytes);
        }
        _image.InvalidateVisual();
    }

    private void OnSkinChanged()
    {
        _skin = null;
        _renderer = null;
        var variant = SkinVariant.Classic;

        try
        {
            using var bitmap = DecodeStraightRgba(SkinPngSource) ?? DecodeLegacyBitmap(SkinSource);
            if (bitmap is not null)
            {
                _skin = SkinTexture.FromRgba(bitmap.Width, bitmap.Height, bitmap.GetPixelSpan());
                var slim = IsSlim ?? SkinTypeChecker.GetTextType(bitmap) == SkinType.NewSlim;
                variant = slim ? SkinVariant.Slim : SkinVariant.Classic;
            }
        }
        catch (Exception ex)
        {
            Log("Skin texture could not be loaded; preview cleared.", ex);
            _skin = null;
        }

        if (_skin is null)
        {
            _image.Source = null;
            _frame?.Dispose();
            _frame = null;
            return;
        }

        _renderer = new PlayerSkinRenderer(_skin, variant) { Cape = _cape };
        _dirty = true;
    }

    private void OnCapeChanged()
    {
        _cape = null;
        try
        {
            using var bitmap = DecodeStraightRgba(CapePngSource);
            if (bitmap is not null)
                _cape = CapeTexture.FromRgba(bitmap.Width, bitmap.Height, bitmap.GetPixelSpan());
        }
        catch (Exception ex)
        {
            Log("Cape texture could not be loaded; showing the player without a cape.", ex);
        }

        if (_renderer is not null) _renderer.Cape = _cape;
        _dirty = true;
    }

    // Decode straight to non-premultiplied RGBA so semi-transparent second-layer pixels keep
    // their exact colour (a premultiplied round-trip darkens them).
    private static SKBitmap? DecodeStraightRgba(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        using var codec = SKCodec.Create(new MemoryStream(png));
        if (codec is null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        return SKBitmap.Decode(codec, info);
    }

    private static SKBitmap? DecodeLegacyBitmap(Bitmap? bitmap)
    {
        if (bitmap is null) return null;
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return DecodeStraightRgba(ms.ToArray());
    }

    private void ResetView()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _dirty = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is ToggleButton) return;
        _dragFrom = e.GetPosition(this);
        e.Pointer.Capture((IInputElement?)sender);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        var now = e.GetPosition(this);
        (_yaw, _pitch) = PlayerOrbit.Drag(_yaw, _pitch, now.X - from.X, now.Y - from.Y);
        _dragFrom = now;
        _dirty = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragFrom = null;
        e.Pointer.Capture(null);
    }
}
