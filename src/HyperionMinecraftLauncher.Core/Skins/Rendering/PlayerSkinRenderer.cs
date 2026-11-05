using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

/// <summary>
/// Software renderer for the full 3D player model: head, body, both arms and legs, the second
/// (hat / jacket / sleeve / pants) layer and an optional cape. Each triangle is filled row by
/// row: the span of pixel centres inside it is solved from its edge equations and depth / UV
/// are stepped incrementally, with a 1/z depth buffer resolving overlap per sample. Spans are
/// widened by a hair so neighbouring triangles overlap rather than leave cracks. Texture
/// coordinates are perspective-correct and sampled nearest-neighbour like the game. The frame
/// is optionally supersampled and box-filtered down to premultiplied BGRA with a transparent
/// background. No GPU is involved; one instance must not render from two threads at once.
/// </summary>
public sealed class PlayerSkinRenderer
{
    // Model units are skin pixels: the player is 32 tall, feet at y = 0, facing +Z.
    private const float ModelCenterY = 16f;
    private const float CameraDistance = 60f;
    private const float SpanSlack = 1e-3f;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.35f, 0.55f, 1f));

    // Cape net: 10x16x1 box at (0,0) of the cape texture, built at the player's back.
    private static readonly ModelBox CapeBox = new(new Vector3(-5, 8, -3), 10, 16, 1, 0, 0, 0f, true, Limb.Body);
    private static readonly BoxFace[] CapeFaces = CapeBox.Faces().ToArray();

    private readonly SkinTexture _texture;
    private readonly (ModelBox Box, BoxFace[] Faces)[] _parts;

    private float[] _depth = Array.Empty<float>();
    private uint[] _color = Array.Empty<uint>();
    private int _bufferWidth;
    private int _bufferHeight;
    // Sample-space rectangle touched by the previous frame; only it needs clearing.
    private int _dirtyMinX, _dirtyMinY, _dirtyMaxX = -1, _dirtyMaxY = -1;

    /// <summary>Build a renderer for one skin and arm model.</summary>
    public PlayerSkinRenderer(SkinTexture texture, SkinVariant variant)
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _parts = ModelBox.PlayerModel(variant).Select(b => (b, b.Faces().ToArray())).ToArray();
    }

    /// <summary>Shade faces by their angle to a front-top light. Off renders flat texture colours.</summary>
    public bool Lighting { get; set; } = true;

    /// <summary>Draw the second skin layer (hat, jacket, sleeves, pants).</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Samples per pixel along each axis; 2 smooths the silhouette, 1 is fastest.</summary>
    public int Supersampling { get; set; } = 2;

    /// <summary>Cape hung from the shoulders, or null for none.</summary>
    public CapeTexture? Cape { get; set; }

    /// <summary>
    /// Render one frame into <paramref name="bgraPremultiplied"/> (width * height * 4 bytes,
    /// row-major, premultiplied BGRA, transparent background).
    /// </summary>
    public void Render(PlayerPose pose, Span<byte> bgraPremultiplied, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (bgraPremultiplied.Length < width * height * 4) throw new ArgumentException("Output buffer too small.", nameof(bgraPremultiplied));

        var ss = Math.Clamp(Supersampling, 1, 4);
        var sw = width * ss;
        var sh = height * ss;
        PrepareBuffers(sw, sh);

        // Fit the 32-unit tall model (plus room for swinging limbs) into the frame.
        var unitsToPixels = Math.Min(sh / 38f, sw / 26f);
        var camera = new Camera(sw, sh, unitsToPixels * CameraDistance, pose.YawDegrees, pose.PitchDegrees);

        var skin = new TextureRef(_texture.Pixels, _texture.Width, _texture.Height, _texture.Scale);
        foreach (var (box, faces) in _parts)
        {
            if (box.Overlay && !ShowOverlay) continue;
            var (pivot, angle) = LimbTransform(box.Limb, pose.LimbSwing);
            var place = angle == 0
                ? Matrix4x4.Identity
                : Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(pivot);
            DrawFaces(faces, place * camera.View, skin, cutout: box.Overlay, camera);
        }

        if (Cape is { } cape)
        {
            // Minecraft's cape box, turned 180 degrees so its outer side faces away from the
            // player, hung from the shoulders and blown back a little more with each stride.
            var hinge = new Vector3(0, 24, -2);
            var place = Matrix4x4.CreateScale(-1, 1, -1) * Matrix4x4.CreateTranslation(0, 0, -5)
                * Matrix4x4.CreateTranslation(-hinge)
                * Matrix4x4.CreateRotationX(0.12f + 0.45f * Math.Abs(pose.LimbSwing))
                * Matrix4x4.CreateTranslation(hinge);
            DrawFaces(CapeFaces, place * camera.View, new TextureRef(cape.Pixels, cape.Width, cape.Height, cape.Scale), cutout: true, camera);
        }

        Downsample(bgraPremultiplied, width, height, ss);
    }

    private void PrepareBuffers(int sw, int sh)
    {
        if (sw != _bufferWidth || sh != _bufferHeight)
        {
            if (_depth.Length < sw * sh)
            {
                _depth = new float[sw * sh];
                _color = new uint[sw * sh];
            }
            else
            {
                Array.Clear(_depth, 0, sw * sh);
                Array.Clear(_color, 0, sw * sh);
            }
            _bufferWidth = sw;
            _bufferHeight = sh;
        }
        else if (_dirtyMaxX >= _dirtyMinX)
        {
            var count = _dirtyMaxX - _dirtyMinX + 1;
            for (var y = _dirtyMinY; y <= _dirtyMaxY; y++)
            {
                _depth.AsSpan(y * sw + _dirtyMinX, count).Clear();
                _color.AsSpan(y * sw + _dirtyMinX, count).Clear();
            }
        }

        _dirtyMinX = int.MaxValue;
        _dirtyMinY = int.MaxValue;
        _dirtyMaxX = -1;
        _dirtyMaxY = -1;
    }

    private static (Vector3 Pivot, float AngleX) LimbTransform(Limb limb, float swing) => limb switch
    {
        Limb.RightArm => (new Vector3(-5, 22, 0), swing * 0.8f),
        Limb.LeftArm => (new Vector3(5, 22, 0), -swing * 0.8f),
        Limb.RightLeg => (new Vector3(-2, 12, 0), -swing),
        Limb.LeftLeg => (new Vector3(2, 12, 0), swing),
        _ => (Vector3.Zero, 0f),
    };

    private void DrawFaces(BoxFace[] faces, Matrix4x4 modelToView, TextureRef texture, bool cutout, Camera camera)
    {
        Span<Vector3> view = stackalloc Vector3[4];

        foreach (var face in faces)
        {
            for (var i = 0; i < 4; i++)
                view[i] = Vector3.Transform(face.Corners[i], modelToView);

            // Outward normal (corners run TL, TR, BR, BL seen from outside), then back-face cull
            // against the vector to the camera so it stays right under perspective.
            var normal = Vector3.Cross(view[3] - view[0], view[1] - view[0]);
            var toCamera = new Vector3(0, 0, CameraDistance) - view[0];
            if (Vector3.Dot(normal, toCamera) <= 0) continue;

            var shade = 1f;
            if (Lighting)
            {
                var n = Vector3.Normalize(normal);
                shade = 0.55f + 0.45f * Math.Max(0f, Vector3.Dot(n, LightDirection));
            }

            var k = texture.Scale;
            var rect = new TexRect(face.U0 * k, face.V0 * k, Math.Min(face.U1 * k, texture.Width), Math.Min(face.V1 * k, texture.Height));
            if (rect.U1 <= rect.U0 || rect.V1 <= rect.V0) continue;

            var tl = camera.Project(view[0], rect.U0, rect.V0);
            var tr = camera.Project(view[1], rect.U1, rect.V0);
            var br = camera.Project(view[2], rect.U1, rect.V1);
            var bl = camera.Project(view[3], rect.U0, rect.V1);
            DrawTriangle(tl, tr, br, rect, texture, cutout, shade);
            DrawTriangle(tl, br, bl, rect, texture, cutout, shade);
        }
    }

    private void DrawTriangle(in ScreenVertex a, in ScreenVertex b, in ScreenVertex c, TexRect rect, TextureRef texture, bool cutout, float shade)
    {
        var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(area) < 1e-6f) return;
        var inv = 1f / area;

        // Normalised edge functions w_i(x, y) = A_i x + B_i y + C_i (barycentric weight of
        // vertex i); a sample is inside when all three are >= 0.
        var e0 = EdgePlane(b, c, inv);
        var e1 = EdgePlane(c, a, inv);
        var e2 = EdgePlane(a, b, inv);

        // Attributes are linear in screen space: value = Dx x + Dy y + Dc.
        var z = AttributePlane(e0, e1, e2, a.InvZ, b.InvZ, c.InvZ);
        var uz = AttributePlane(e0, e1, e2, a.UOverZ, b.UOverZ, c.UOverZ);
        var vz = AttributePlane(e0, e1, e2, a.VOverZ, b.VOverZ, c.VOverZ);

        var sw = _bufferWidth;
        var minX = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        var maxX = Math.Min(sw - 1, (int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        var minY = Math.Max(0, (int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        var maxY = Math.Min(_bufferHeight - 1, (int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY) return;

        var pixels = texture.Pixels;
        var texWidth = texture.Width;
        var depth = _depth;
        var color = _color;

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            float left = minX, right = maxX + 1f;
            if (!ClipSpan(e0, py, ref left, ref right) || !ClipSpan(e1, py, ref left, ref right) || !ClipSpan(e2, py, ref left, ref right))
                continue;

            var x0 = Math.Max(minX, (int)MathF.Ceiling(left - 0.5f));
            var x1 = Math.Min(maxX, (int)MathF.Floor(right - 0.5f));
            if (x0 > x1) continue;

            if (x0 < _dirtyMinX) _dirtyMinX = x0;
            if (x1 > _dirtyMaxX) _dirtyMaxX = x1;
            if (y < _dirtyMinY) _dirtyMinY = y;
            if (y > _dirtyMaxY) _dirtyMaxY = y;

            var px = x0 + 0.5f;
            var invZ = z.Dx * px + z.Dy * py + z.Dc;
            var u = uz.Dx * px + uz.Dy * py + uz.Dc;
            var v = vz.Dx * px + vz.Dy * py + vz.Dc;
            var row = y * sw;

            for (var x = x0; x <= x1; x++, invZ += z.Dx, u += uz.Dx, v += vz.Dx)
            {
                var index = row + x;
                if (invZ <= depth[index]) continue;

                var w = 1f / invZ;
                var tx = Math.Clamp((int)(u * w), rect.U0, rect.U1 - 1);
                var ty = Math.Clamp((int)(v * w), rect.V0, rect.V1 - 1);
                var t = (ty * texWidth + tx) * 4;

                // The base layer is opaque in-game; the second layer and the cape are cut out
                // where transparent.
                if (cutout && pixels[t + 3] < 128) continue;

                var r = (uint)(pixels[t] * shade);
                var g = (uint)(pixels[t + 1] * shade);
                var bl = (uint)(pixels[t + 2] * shade);
                color[index] = 0xFF000000u | (r << 16) | (g << 8) | bl;
                depth[index] = invZ;
            }
        }
    }

    // Narrow [left, right] to where A x + (B py + C) >= 0; false if the row misses the triangle.
    private static bool ClipSpan(in Plane e, float py, ref float left, ref float right)
    {
        var rest = e.Dy * py + e.Dc;
        if (e.Dx > 1e-9f)
        {
            var bound = -rest / e.Dx - SpanSlack;
            if (bound > left) left = bound;
        }
        else if (e.Dx < -1e-9f)
        {
            var bound = -rest / e.Dx + SpanSlack;
            if (bound < right) right = bound;
        }
        else if (rest < -1e-6f)
        {
            return false;
        }
        return left <= right;
    }

    private static Plane EdgePlane(in ScreenVertex p0, in ScreenVertex p1, float inv) => new(
        -(p1.Y - p0.Y) * inv,
        (p1.X - p0.X) * inv,
        ((p1.Y - p0.Y) * p0.X - (p1.X - p0.X) * p0.Y) * inv);

    private static Plane AttributePlane(in Plane e0, in Plane e1, in Plane e2, float a0, float a1, float a2) => new(
        e0.Dx * a0 + e1.Dx * a1 + e2.Dx * a2,
        e0.Dy * a0 + e1.Dy * a1 + e2.Dy * a2,
        e0.Dc * a0 + e1.Dc * a1 + e2.Dc * a2);

    private void Downsample(Span<byte> output, int width, int height, int ss)
    {
        output[..(width * height * 4)].Clear();
        if (_dirtyMaxX < _dirtyMinX) return;

        var sw = _bufferWidth;
        var samples = ss * ss;
        var fromX = _dirtyMinX / ss;
        var toX = Math.Min(width - 1, _dirtyMaxX / ss);
        var fromY = _dirtyMinY / ss;
        var toY = Math.Min(height - 1, _dirtyMaxY / ss);

        for (var y = fromY; y <= toY; y++)
            for (var x = fromX; x <= toX; x++)
            {
                int a = 0, r = 0, g = 0, b = 0;
                for (var j = 0; j < ss; j++)
                {
                    var row = (y * ss + j) * sw + x * ss;
                    for (var i = 0; i < ss; i++)
                    {
                        var p = _color[row + i];
                        if (p == 0) continue;
                        a += 255;
                        r += (int)((p >> 16) & 0xFF);
                        g += (int)((p >> 8) & 0xFF);
                        b += (int)(p & 0xFF);
                    }
                }
                if (a == 0) continue;
                var o = (y * width + x) * 4;
                // Opaque samples carry straight colour; their average over all samples is
                // exactly the premultiplied value.
                output[o] = (byte)(b / samples);
                output[o + 1] = (byte)(g / samples);
                output[o + 2] = (byte)(r / samples);
                output[o + 3] = (byte)(a / samples);
            }
    }

    private readonly record struct Plane(float Dx, float Dy, float Dc);

    private readonly record struct TextureRef(byte[] Pixels, int Width, int Height, int Scale);

    private readonly record struct ScreenVertex(float X, float Y, float InvZ, float UOverZ, float VOverZ);

    private readonly record struct TexRect(int U0, int V0, int U1, int V1);

    private readonly struct Camera
    {
        private readonly float _focal;
        private readonly float _cx;
        private readonly float _cy;

        public Camera(int width, int height, float focal, float yawDegrees, float pitchDegrees)
        {
            // Centre the model, turn it, then tilt its top toward the camera for a positive pitch.
            View = Matrix4x4.CreateTranslation(0, -ModelCenterY, 0)
                * Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180f)
                * Matrix4x4.CreateRotationX(-pitchDegrees * MathF.PI / 180f);
            _focal = focal;
            _cx = width / 2f;
            _cy = height / 2f;
        }

        /// <summary>Model space to camera space (camera on +Z looking at the origin).</summary>
        public Matrix4x4 View { get; }

        public ScreenVertex Project(Vector3 view, float u, float v)
        {
            var invZ = 1f / (CameraDistance - view.Z);
            return new ScreenVertex(
                _cx + view.X * _focal * invZ,
                _cy - view.Y * _focal * invZ,
                invZ,
                u * invZ,
                v * invZ);
        }
    }
}

/// <summary>Body part a model box belongs to, used to swing it around its joint.</summary>
internal enum Limb { Head, Body, RightArm, LeftArm, RightLeg, LeftLeg }

/// <summary>
/// One textured cuboid of the player model. <c>U</c>/<c>V</c> is the top-left of its net in
/// the 64x64 skin layout and <c>W</c>/<c>H</c>/<c>D</c> its size in texels (the same box UV
/// scheme as Minecraft's ModelPart cubes); <c>Inflate</c> grows the second-layer boxes.
/// </summary>
internal readonly record struct ModelBox(Vector3 Min, int W, int H, int D, int U, int V, float Inflate, bool Overlay, Limb Limb)
{
    public static IReadOnlyList<ModelBox> PlayerModel(SkinVariant variant)
    {
        var arm = variant == SkinVariant.Slim ? 3 : 4;
        return new[]
        {
            new ModelBox(new Vector3(-4, 24, -4), 8, 8, 8, 0, 0, 0f, false, Limb.Head),
            new ModelBox(new Vector3(-4, 12, -2), 8, 12, 4, 16, 16, 0f, false, Limb.Body),
            new ModelBox(new Vector3(-4 - arm, 12, -2), arm, 12, 4, 40, 16, 0f, false, Limb.RightArm),
            new ModelBox(new Vector3(4, 12, -2), arm, 12, 4, 32, 48, 0f, false, Limb.LeftArm),
            new ModelBox(new Vector3(-4, 0, -2), 4, 12, 4, 0, 16, 0f, false, Limb.RightLeg),
            new ModelBox(new Vector3(0, 0, -2), 4, 12, 4, 16, 48, 0f, false, Limb.LeftLeg),

            new ModelBox(new Vector3(-4, 24, -4), 8, 8, 8, 32, 0, 0.5f, true, Limb.Head),
            new ModelBox(new Vector3(-4, 12, -2), 8, 12, 4, 16, 32, 0.25f, true, Limb.Body),
            new ModelBox(new Vector3(-4 - arm, 12, -2), arm, 12, 4, 40, 32, 0.25f, true, Limb.RightArm),
            new ModelBox(new Vector3(4, 12, -2), arm, 12, 4, 48, 48, 0.25f, true, Limb.LeftArm),
            new ModelBox(new Vector3(-4, 0, -2), 4, 12, 4, 0, 32, 0.25f, true, Limb.RightLeg),
            new ModelBox(new Vector3(0, 0, -2), 4, 12, 4, 0, 48, 0.25f, true, Limb.LeftLeg),
        };
    }

    /// <summary>
    /// The six faces, each with corners in top-left, top-right, bottom-right, bottom-left order
    /// as seen from outside the box, and the matching rectangle of the skin net.
    /// </summary>
    public IEnumerable<BoxFace> Faces()
    {
        float x0 = Min.X - Inflate, y0 = Min.Y - Inflate, z0 = Min.Z - Inflate;
        float x1 = Min.X + W + Inflate, y1 = Min.Y + H + Inflate, z1 = Min.Z + D + Inflate;
        int u = U, v = V, w = W, h = H, d = D;

        // Front (+Z): the face / chest side of the net.
        yield return new BoxFace(new[] { new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) }, u + d, v + d, u + d + w, v + d + h);
        // Back (-Z).
        yield return new BoxFace(new[] { new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0) }, u + 2 * d + w, v + d, u + 2 * d + 2 * w, v + d + h);
        // -X side (the player's right).
        yield return new BoxFace(new[] { new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), new Vector3(x0, y0, z0) }, u, v + d, u + d, v + d + h);
        // +X side (the player's left).
        yield return new BoxFace(new[] { new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1) }, u + d + w, v + d, u + 2 * d + w, v + d + h);
        // Top.
        yield return new BoxFace(new[] { new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1) }, u + d, v, u + d + w, v + d);
        // Bottom (net is flipped vertically relative to the top).
        yield return new BoxFace(new[] { new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0) }, u + d + w, v, u + d + 2 * w, v + d);
    }
}

/// <summary>A box face: four model-space corners and its skin-net rectangle in 64x64 units.</summary>
internal readonly record struct BoxFace(Vector3[] Corners, int U0, int V0, int U1, int V1);
