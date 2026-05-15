using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// 3D viewer for a 64x64 (or 64x32 legacy) Minecraft player skin. Renders the head,
/// torso, both arms and both legs as textured cuboids, painter's-order sorted, on top
/// of the live Avalonia Skia canvas via <see cref="ICustomDrawOperation"/>. Mouse-drag
/// rotates the model.
/// </summary>
/// <remarks>
/// Approach (per the research report): we lease the underlying <see cref="SKCanvas"/> from
/// Avalonia, build a perspective + yaw/pitch 4x4 with <see cref="SKMatrix44"/>, then for
/// each of the 36 faces (6 cuboids x 6 faces) we compute a local matrix that translates
/// + rotates the face onto its cuboid wall, multiply by the world matrix, collapse to a
/// 3x3 <see cref="SKMatrix"/>, and <c>DrawBitmap</c> the face's UV sub-rect into a unit
/// quad. <see cref="SKPaint.FilterQuality"/> = None keeps the skin's pixel art crisp.
/// </remarks>
public sealed class SkinViewer3D : Control
{
    /// <summary>Avalonia bitmap holding the skin PNG. The control round-trips it to an SKBitmap on load.</summary>
    public static readonly StyledProperty<Bitmap?> SkinProperty =
        AvaloniaProperty.Register<SkinViewer3D, Bitmap?>(nameof(Skin));

    public Bitmap? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    private SKBitmap? _skSkin;
    private bool _isLegacy;       // 64x32 layout
    private float _yaw = 25f;
    private float _pitch = 8f;
    private Point? _lastPointer;

    static SkinViewer3D()
    {
        AffectsRender<SkinViewer3D>(SkinProperty);
        SkinProperty.Changed.AddClassHandler<SkinViewer3D>((s, _) => s.ReloadBitmap());
    }

    public SkinViewer3D()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    private void ReloadBitmap()
    {
        _skSkin?.Dispose();
        _skSkin = null;
        _isLegacy = false;

        if (Skin is null) return;

        using var ms = new MemoryStream();
        Skin.Save(ms);
        ms.Position = 0;
        _skSkin = SKBitmap.Decode(ms);
        if (_skSkin is { Height: 32 })
            _isLegacy = true;

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_lastPointer is { } prev && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var now = e.GetPosition(this);
            _yaw += (float)(now.X - prev.X) * 0.6f;
            _pitch += (float)(now.Y - prev.Y) * 0.4f;
            _pitch = Math.Clamp(_pitch, -60f, 60f);
            _lastPointer = now;
            InvalidateVisual();
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _lastPointer = null;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    public override void Render(DrawingContext context)
    {
        if (_skSkin is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.Custom(new PlayerDrawOp(new Rect(Bounds.Size), _skSkin, _yaw, _pitch, _isLegacy));
    }

    // ===================== custom Skia draw op =====================
    private sealed class PlayerDrawOp : ICustomDrawOperation
    {
        // UV rectangles on the 64x64 skin atlas (x, y, w, h pixels) for each face of each part.
        // Reference: https://minecraft.wiki/w/Skin
        private static readonly Cuboid Head = new(
            uw: 8, uh: 8, ud: 8,
            top:    SKRect.Create(8, 0, 8, 8),
            bottom: SKRect.Create(16, 0, 8, 8),
            front:  SKRect.Create(8, 8, 8, 8),
            right:  SKRect.Create(16, 8, 8, 8),
            back:   SKRect.Create(24, 8, 8, 8),
            left:   SKRect.Create(0, 8, 8, 8));

        private static readonly Cuboid Body = new(
            uw: 8, uh: 12, ud: 4,
            top:    SKRect.Create(20, 16, 8, 4),
            bottom: SKRect.Create(28, 16, 8, 4),
            front:  SKRect.Create(20, 20, 8, 12),
            right:  SKRect.Create(28, 20, 4, 12),
            back:   SKRect.Create(32, 20, 8, 12),
            left:   SKRect.Create(16, 20, 4, 12));

        private static readonly Cuboid RightArm = new(
            uw: 4, uh: 12, ud: 4,
            top:    SKRect.Create(44, 16, 4, 4),
            bottom: SKRect.Create(48, 16, 4, 4),
            front:  SKRect.Create(44, 20, 4, 12),
            right:  SKRect.Create(48, 20, 4, 12),
            back:   SKRect.Create(52, 20, 4, 12),
            left:   SKRect.Create(40, 20, 4, 12));

        private static readonly Cuboid LeftArm = new(
            uw: 4, uh: 12, ud: 4,
            top:    SKRect.Create(36, 48, 4, 4),
            bottom: SKRect.Create(40, 48, 4, 4),
            front:  SKRect.Create(36, 52, 4, 12),
            right:  SKRect.Create(40, 52, 4, 12),
            back:   SKRect.Create(44, 52, 4, 12),
            left:   SKRect.Create(32, 52, 4, 12));

        private static readonly Cuboid RightLeg = new(
            uw: 4, uh: 12, ud: 4,
            top:    SKRect.Create(4, 16, 4, 4),
            bottom: SKRect.Create(8, 16, 4, 4),
            front:  SKRect.Create(4, 20, 4, 12),
            right:  SKRect.Create(8, 20, 4, 12),
            back:   SKRect.Create(12, 20, 4, 12),
            left:   SKRect.Create(0, 20, 4, 12));

        private static readonly Cuboid LeftLeg = new(
            uw: 4, uh: 12, ud: 4,
            top:    SKRect.Create(20, 48, 4, 4),
            bottom: SKRect.Create(24, 48, 4, 4),
            front:  SKRect.Create(20, 52, 4, 12),
            right:  SKRect.Create(24, 52, 4, 12),
            back:   SKRect.Create(28, 52, 4, 12),
            left:   SKRect.Create(16, 52, 4, 12));

        private readonly SKBitmap _skin;
        private readonly float _yaw, _pitch;
        private readonly bool _legacy;

        public PlayerDrawOp(Rect bounds, SKBitmap skin, float yaw, float pitch, bool legacy)
        {
            Bounds = bounds;
            _skin = skin;
            _yaw = yaw;
            _pitch = pitch;
            _legacy = legacy;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => Bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;

            float w = (float)Bounds.Width;
            float h = (float)Bounds.Height;
            float cx = w / 2f;
            float cy = h / 2f;

            // The full player is ~32 minecraft-pixels tall (8 head + 12 body + 12 leg). Scale so it fits.
            float unit = Math.Min(w / 18f, h / 38f);     // size of 1 MC pixel in screen pixels
            float depth = 256f * unit;                    // far enough that perspective is gentle

            // World matrix: pitch (around X) then yaw (around Y), then perspective.
            using var world = SKMatrix44.CreateIdentity();
            world.PostConcat(SKMatrix44.CreateRotationDegrees(1, 0, 0, _pitch));
            world.PostConcat(SKMatrix44.CreateRotationDegrees(0, 1, 0, _yaw));
            using var persp = SKMatrix44.CreateIdentity();
            persp[3, 2] = -1f / depth;
            world.PostConcat(persp);

            using var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.None,
                IsAntialias = false,
            };

            // y axis goes down in Skia screen space; we place y=0 at the model centre.
            // Body pivot at y=0; head sits above (negative y in Skia), legs below.
            //
            // Layout (MC pixels, head/body/legs stacked):
            //   head:    centred at (0, -10, 0),  half-size 4
            //   body:    centred at (0,  -2, 0),  half-size (4, 6, 2)
            //   r/l arm: centred at (+/-6, -2, 0), half-size (2, 6, 2)
            //   r/l leg: centred at (+/-2,  10, 0), half-size (2, 6, 2)

            // Collect all faces from all parts, depth-sorted, then draw back-to-front.
            // (Heap-allocated because FacePlan holds an SKMatrix44 reference - stackalloc
            // only allows unmanaged element types.)
            var faces = new FacePlan[36];
            int n = 0;
            var span = faces.AsSpan();
            n += EnqueueFaces(span[n..], Head,     pivotX:  0, pivotY: -10, pivotZ:  0, hx: 4, hy: 4, hz: 4, world);
            n += EnqueueFaces(span[n..], Body,     pivotX:  0, pivotY:  -2, pivotZ:  0, hx: 4, hy: 6, hz: 2, world);
            n += EnqueueFaces(span[n..], RightArm, pivotX: -6, pivotY:  -2, pivotZ:  0, hx: 2, hy: 6, hz: 2, world);
            // legacy 64x32 atlas mirrors right arm/leg into the left slots.
            n += EnqueueFaces(span[n..], _legacy ? RightArm : LeftArm, pivotX:  6, pivotY: -2, pivotZ: 0, hx: 2, hy: 6, hz: 2, world);
            n += EnqueueFaces(span[n..], RightLeg, pivotX: -2, pivotY:  10, pivotZ:  0, hx: 2, hy: 6, hz: 2, world);
            n += EnqueueFaces(span[n..], _legacy ? RightLeg : LeftLeg, pivotX:  2, pivotY: 10, pivotZ: 0, hx: 2, hy: 6, hz: 2, world);

            // Painter's algorithm: sort the active slice by depth (centre.Z) descending.
            // Bubble sort for a small n - n=36, but it's still trivial.
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (faces[i].Z < faces[j].Z)
                        (faces[i], faces[j]) = (faces[j], faces[i]);

            canvas.Save();
            canvas.Translate(cx, cy);
            canvas.Scale(unit, unit);          // 1 MC-pixel = `unit` screen-pixels
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var plan = faces[i];
                    canvas.Save();
                    var m = plan.LocalToWorld.Matrix;
                    canvas.Concat(ref m);
                    canvas.DrawBitmap(_skin, plan.Uv, plan.DestQuad, paint);
                    canvas.Restore();
                }
            }
            finally
            {
                canvas.Restore();
            }
        }

        private static int EnqueueFaces(
            Span<FacePlan> dest,
            Cuboid cuboid,
            float pivotX, float pivotY, float pivotZ,
            float hx, float hy, float hz,
            SKMatrix44 world)
        {
            // Each face: build local matrix that places a unit quad at the centre of one
            // face of the (2hx x 2hy x 2hz) box centred at (pivotX, pivotY, pivotZ).
            // Quad lives at z=0 in local space, oriented in XY; the rotation moves it
            // onto the right face, then we translate by half-size + pivot.

            int n = 0;
            // Front: +Z face  (faces camera at yaw=0)
            dest[n++] = MakeFace(cuboid.Front,  pivotX, pivotY, pivotZ,  0,   0,   0,    hx,  hy, 0, hz,  world);
            // Back:  -Z face (rotate 180 around Y)
            dest[n++] = MakeFace(cuboid.Back,   pivotX, pivotY, pivotZ,  0, 180,  0,    hx,  hy, 0, -hz, world);
            // Right: +X face (rotate -90 around Y) - but MC's "right" from viewer means texture face.
            dest[n++] = MakeFace(cuboid.Right,  pivotX, pivotY, pivotZ,  0, -90, 0,    hz,  hy, 0, -hx, world);
            // Left: -X face (rotate +90 around Y)
            dest[n++] = MakeFace(cuboid.Left,   pivotX, pivotY, pivotZ,  0,  90, 0,    hz,  hy, 0,  hx, world);
            // Top: -Y face (rotate +90 around X)
            dest[n++] = MakeFace(cuboid.Top,    pivotX, pivotY, pivotZ,  90,   0, 0,    hx,  hz, -hy, 0, world);
            // Bottom: +Y face (rotate -90 around X)
            dest[n++] = MakeFace(cuboid.Bottom, pivotX, pivotY, pivotZ, -90,   0, 0,    hx,  hz,  hy, 0, world);
            return n;
        }

        private static FacePlan MakeFace(
            SKRect uv,
            float pivotX, float pivotY, float pivotZ,
            float rotX, float rotY, float rotZ,
            float quadHalfW, float quadHalfH,
            float offsetY, float offsetZ,
            SKMatrix44 world)
        {
            // Build local-to-world: scale-quad -> rotate-to-face -> translate-onto-cube -> apply-world.
            var local = SKMatrix44.CreateIdentity();
            if (rotX != 0) local.PostConcat(SKMatrix44.CreateRotationDegrees(1, 0, 0, rotX));
            if (rotY != 0) local.PostConcat(SKMatrix44.CreateRotationDegrees(0, 1, 0, rotY));
            if (rotZ != 0) local.PostConcat(SKMatrix44.CreateRotationDegrees(0, 0, 1, rotZ));
            local.PostConcat(SKMatrix44.CreateTranslate(pivotX, pivotY + offsetY, pivotZ + offsetZ));
            local.PostConcat(world);

            // Centre of the face in world space (used for painter's-algorithm sort).
            // SKMatrix44 doesn't expose a 3D MapPoint in SkiaSharp 2.88, so we compute
            // the Z component directly from the third row of the 4x4.
            float cx = pivotX;
            float cy = pivotY + offsetY;
            float cz = pivotZ + offsetZ;
            float worldZ = world[2, 0] * cx + world[2, 1] * cy + world[2, 2] * cz + world[2, 3];

            var dest = new SKRect(-quadHalfW, -quadHalfH, quadHalfW, quadHalfH);
            return new FacePlan(uv, dest, local, worldZ);
        }

        private readonly record struct Cuboid(
            float uw, float uh, float ud,
            SKRect top, SKRect bottom, SKRect front, SKRect right, SKRect back, SKRect left)
        {
            public SKRect Top => top; public SKRect Bottom => bottom; public SKRect Front => front;
            public SKRect Right => right; public SKRect Back => back; public SKRect Left => left;
        }

        private readonly record struct FacePlan(SKRect Uv, SKRect DestQuad, SKMatrix44 LocalToWorld, float Z);
    }
}
