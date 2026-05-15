using System;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MinecraftSkinRender;
using MinecraftSkinRender.OpenGL;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls.SkinRender;

/// <summary>
/// Avalonia <see cref="OpenGlControlBase"/> wrapping <c>Coloryr/MinecraftSkinRender.OpenGL</c>
/// (MIT, on NuGet as <c>MinecraftSkinRender.OpenGL</c> 1.2.0). Mouse-drag rotates the model;
/// the scroll wheel zooms in/out.
///
/// Set <see cref="SkinSource"/> to the player's skin <see cref="Bitmap"/> - the control
/// round-trips it through PNG to an <see cref="SKBitmap"/> and feeds it to the renderer.
/// </summary>
public sealed class AvaloniaSkinViewer : OpenGlControlBase
{
    public static readonly StyledProperty<Bitmap?> SkinSourceProperty =
        AvaloniaProperty.Register<AvaloniaSkinViewer, Bitmap?>(nameof(SkinSource));

    public Bitmap? SkinSource
    {
        get => GetValue(SkinSourceProperty);
        set => SetValue(SkinSourceProperty, value);
    }

    public static readonly StyledProperty<Bitmap?> CapeSourceProperty =
        AvaloniaProperty.Register<AvaloniaSkinViewer, Bitmap?>(nameof(CapeSource));

    public Bitmap? CapeSource
    {
        get => GetValue(CapeSourceProperty);
        set => SetValue(CapeSourceProperty, value);
    }

    public static readonly StyledProperty<bool> SlimProperty =
        AvaloniaProperty.Register<AvaloniaSkinViewer, bool>(nameof(Slim));

    public bool Slim
    {
        get => GetValue(SlimProperty);
        set => SetValue(SlimProperty, value);
    }

    private SkinRenderOpenGL? _renderer;
    private SKBitmap? _pendingSkin;
    private SKBitmap? _pendingCape;
    private bool _pendingSlim;
    private bool _haveNewSkin;
    private bool _haveNewCape;
    private DateTime _lastTick = DateTime.UtcNow;
    private DispatcherTimer? _tickTimer;

    static AvaloniaSkinViewer()
    {
        // Re-render whenever the skin / cape changes so the texture re-uploads.
        SkinSourceProperty.Changed.AddClassHandler<AvaloniaSkinViewer>((s, _) => s.QueueSkinUpdate());
        CapeSourceProperty.Changed.AddClassHandler<AvaloniaSkinViewer>((s, _) => s.QueueCapeUpdate());
        SlimProperty.Changed.AddClassHandler<AvaloniaSkinViewer>((s, e) =>
        {
            if (s._renderer is { } r)
            {
                r.SkinType = (bool)e.NewValue! ? SkinType.NewSlim : SkinType.New;
                s.RequestNextFrameRendering();
            }
        });
    }

    public AvaloniaSkinViewer()
    {
        Focusable = true;
        ClipToBounds = true;
        // 30 fps tick for the walking animation; the renderer reads `Tick(double seconds)`
        // and updates its internal limb angles. We start the timer once init succeeds.
    }

    private void QueueSkinUpdate()
    {
        _pendingSkin?.Dispose();
        _pendingSkin = null;
        if (SkinSource is null) return;

        try
        {
            using var ms = new MemoryStream();
            SkinSource.Save(ms);
            ms.Position = 0;
            _pendingSkin = SKBitmap.Decode(ms);
            _pendingSlim = Slim;
            _haveNewSkin = true;
            RequestNextFrameRendering();
        }
        catch
        {
            // ignore - viewer keeps last successfully-set skin
        }
    }

    private void QueueCapeUpdate()
    {
        _pendingCape?.Dispose();
        _pendingCape = null;
        if (CapeSource is null) return;

        try
        {
            using var ms = new MemoryStream();
            CapeSource.Save(ms);
            ms.Position = 0;
            _pendingCape = SKBitmap.Decode(ms);
            _haveNewCape = true;
            RequestNextFrameRendering();
        }
        catch
        {
            // ignore
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        var api = new AvaloniaGlApi(gl);
        _renderer = new SkinRenderOpenGL(api)
        {
            Width = (int)Math.Max(1, Bounds.Width),
            Height = (int)Math.Max(1, Bounds.Height),
            EnableTop = true,
            Animation = true,
        };
        _renderer.SkinType = Slim ? SkinType.NewSlim : SkinType.New;
        _renderer.OpenGlInit();

        // If the user set SkinSource before the GL context existed, push it now.
        if (_pendingSkin is not null) _haveNewSkin = true;
        if (_pendingCape is not null) _haveNewCape = true;

        // Drive the walking animation - SkinRender.Tick wants elapsed seconds.
        _lastTick = DateTime.UtcNow;
        _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, OnTick);
        _tickTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_renderer is null) return;
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _renderer.Tick(dt);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _tickTimer?.Stop();
        _tickTimer = null;
        _renderer?.OpenGlDeinit();
        _renderer = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null) return;
        _renderer.Width = (int)Math.Max(1, Bounds.Width);
        _renderer.Height = (int)Math.Max(1, Bounds.Height);

        if (_haveNewSkin && _pendingSkin is not null)
        {
            _renderer.SkinType = _pendingSlim ? SkinType.NewSlim : SkinType.New;
            _renderer.SetSkinTex(_pendingSkin);
            _haveNewSkin = false;
        }
        if (_haveNewCape && _pendingCape is not null)
        {
            _renderer.SetCapeTex(_pendingCape);
            _renderer.EnableCape = true;
            _haveNewCape = false;
        }

        _renderer.OpenGlRender(fb);
    }

    // ===== input -> renderer.Pointer*  =====
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        var key = e.GetCurrentPoint(this).Properties.IsRightButtonPressed ? KeyType.Right : KeyType.Left;
        _renderer?.PointerPressed(key, new Vector2((float)p.X, (float)p.Y));
        e.Pointer.Capture(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(this);
        _renderer?.PointerReleased(KeyType.Left, new Vector2((float)p.X, (float)p.Y));
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        var key = props.IsRightButtonPressed ? KeyType.Right
                : props.IsLeftButtonPressed ? KeyType.Left
                : KeyType.None;
        _renderer?.PointerMoved(key, new Vector2((float)p.X, (float)p.Y));
        base.OnPointerMoved(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Positive Y delta = scroll up = zoom in.
        _renderer?.PointerWheelChanged(e.Delta.Y > 0);
        base.OnPointerWheelChanged(e);
    }
}
