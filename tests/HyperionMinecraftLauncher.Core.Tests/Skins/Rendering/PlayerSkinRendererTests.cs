using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Rendering;

/// <summary>
/// Tests for the software skin renderer behind the Skins page's walking 3D viewer. Skins are
/// built from flat colour regions so each assertion can tell which texture region a pixel
/// came from.
/// </summary>
public class PlayerSkinRendererTests
{
    private const int W = 120;
    private const int H = 200;

    private static readonly byte[] Green = { 40, 200, 40, 255 };
    private static readonly byte[] Red = { 220, 20, 20, 255 };
    private static readonly byte[] Blue = { 20, 40, 220, 255 };
    private static readonly byte[] Yellow = { 240, 220, 20, 255 };

    [Fact]
    public void FromRgba_Legacy64x32_MirrorsRightLegFrontIntoLeftLeg()
    {
        var skin = new SkinBuilder(64, 32).Fill(0, 0, 64, 32, Green);
        // Right leg front is (4,20) 4x12; paint each column a different red so mirroring shows.
        for (var x = 0; x < 4; x++)
            skin.Fill(4 + x, 20, 1, 12, new byte[] { (byte)(50 + 50 * x), 0, 0, 255 });

        var texture = SkinTexture.FromRgba(64, 32, skin.Pixels);

        Assert.Equal(64, texture.Height);
        // Left leg front lives at (20,52) in the 64x64 layout, mirrored horizontally.
        for (var x = 0; x < 4; x++)
            Assert.Equal((byte)(50 + 50 * (3 - x)), texture.GetRgba(20 + x, 52)[0]);
    }

    [Fact]
    public void FromRgba_RejectsUnsupportedSize()
    {
        Assert.Throws<ArgumentException>(() => SkinTexture.FromRgba(30, 30, new byte[30 * 30 * 4]));
    }

    [Fact]
    public void Render_FrontView_ShowsTheFaceInTheTopOfTheFrame()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).BaseOnly().Fill(8, 8, 8, 8, Red);

        var frame = RenderFrame(skin, new PlayerPose(YawDegrees: 0, PitchDegrees: 0));

        Assert.True(frame.Count(Red, 0, H / 4) > 50, "face should be visible in the top quarter");
        Assert.Equal(0, frame.Count(Red, H / 2, H));
    }

    [Fact]
    public void Render_BackView_ShowsTheBackOfTheHeadNotTheFace()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).BaseOnly().Fill(8, 8, 8, 8, Red).Fill(24, 8, 8, 8, Blue);

        var frame = RenderFrame(skin, new PlayerPose(YawDegrees: 180, PitchDegrees: 0));

        Assert.Equal(0, frame.Count(Red, 0, H));
        Assert.True(frame.Count(Blue, 0, H / 4) > 50);
    }

    [Fact]
    public void Render_OpaqueHat_CoversTheFace()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).Fill(8, 8, 8, 8, Red)
            .Clear(32, 0, 32, 16).Fill(40, 8, 8, 8, Yellow);

        var frame = RenderFrame(skin, new PlayerPose(YawDegrees: 0, PitchDegrees: 0));

        Assert.True(frame.Count(Yellow, 0, H / 4) > 50);
        Assert.Equal(0, frame.Count(Red, 0, H));
    }

    [Fact]
    public void Render_TransparentHat_LeavesTheFaceVisible()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).Fill(8, 8, 8, 8, Red).Clear(32, 0, 32, 16);

        var frame = RenderFrame(skin, new PlayerPose(YawDegrees: 0, PitchDegrees: 0));

        Assert.True(frame.Count(Red, 0, H / 4) > 50);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(35f, 0f)]
    [InlineData(90f, 0f)]
    [InlineData(180f, 0f)]
    public void Render_SolidSkin_SilhouetteHasNoHolesOrCracks(float yaw, float pitch)
    {
        // Base layer only, camera level with the model: from these angles every row of the
        // silhouette is one unbroken run, so any transparent pixel inside it is a raster crack.
        // (Tilted views can show real background between an arm's end and a leg.)
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).BaseOnly();

        var frame = RenderFrame(skin, new PlayerPose(yaw, pitch));

        for (var y = 0; y < H; y++)
            Assert.True(frame.RowIsContiguous(y), $"gap in row {y} at yaw {yaw}, pitch {pitch}");
    }

    [Fact]
    public void Render_SlimVariant_IsNarrowerThanClassic()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green);
        var pose = new PlayerPose(YawDegrees: 0, PitchDegrees: 0);

        var classic = RenderFrame(skin, pose, SkinVariant.Classic);
        var slim = RenderFrame(skin, pose, SkinVariant.Slim);

        var chestRow = H * 45 / 100;
        Assert.True(slim.OpaqueWidth(chestRow) < classic.OpaqueWidth(chestRow));
    }

    [Fact]
    public void Walk_SwingsArmsAndLegsAndStartsNeutral()
    {
        var start = PlayerPose.Walking(yawDegrees: 0, pitchDegrees: 0, seconds: 0);
        var quarter = PlayerPose.Walking(yawDegrees: 0, pitchDegrees: 0, seconds: PlayerPose.WalkCycleSeconds / 4);

        Assert.Equal(0f, start.LimbSwing, 3);
        Assert.True(Math.Abs(quarter.LimbSwing) > 0.3f);
    }

    [Fact]
    public void Render_WalkingPose_DiffersFromStandingPose()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).BaseOnly().Fill(40, 16, 16, 16, Red);
        var standing = RenderFrame(skin, new PlayerPose(YawDegrees: 60, PitchDegrees: 0));
        var walking = RenderFrame(skin, PlayerPose.Walking(60, 0, PlayerPose.WalkCycleSeconds / 4));

        Assert.NotEqual(standing.Count(Red, 0, H), walking.Count(Red, 0, H));
    }

    [Fact]
    public void Render_Cape_ShowsItsOuterSideFromBehindOnly()
    {
        var skin = new SkinBuilder(64, 64).Fill(0, 0, 64, 64, Green).BaseOnly();
        // Cape net: outer side (1,1) 10x16 faces away from the player, inner side (12,1) faces their back.
        var cape = new SkinBuilder(64, 32).Fill(0, 0, 64, 32, Blue).Fill(1, 1, 10, 16, Yellow).Fill(12, 1, 10, 16, Red);
        var capeTexture = CapeTexture.FromRgba(64, 32, cape.Pixels);

        var back = RenderFrame(skin, new PlayerPose(YawDegrees: 180, PitchDegrees: 0), cape: capeTexture);
        var front = RenderFrame(skin, new PlayerPose(YawDegrees: 0, PitchDegrees: 0), cape: capeTexture);

        Assert.True(back.Count(Yellow, 0, H) > 100, "outer side of the cape should cover the back");
        Assert.Equal(0, back.Count(Red, 0, H));
        Assert.Equal(0, front.Count(Yellow, 0, H));
    }

    [Fact]
    public void CapeTexture_AcceptsTheOld22x17Layout()
    {
        var cape = CapeTexture.FromRgba(22, 17, new byte[22 * 17 * 4]);

        Assert.Equal(1, cape.Scale);
    }

    [Fact]
    public void Orbit_DragRightTurnsTheModelAndPitchIsClamped()
    {
        var turned = PlayerOrbit.Drag(yawDegrees: 170, pitchDegrees: 10, dx: 40, dy: 0);
        var tilted = PlayerOrbit.Drag(yawDegrees: 0, pitchDegrees: 10, dx: 0, dy: 1000);

        Assert.InRange(turned.Yaw, -180f, 180f);
        Assert.Equal(170f + 40 * PlayerOrbit.DegreesPerPixel - 360f, turned.Yaw, 3);
        Assert.Equal(PlayerOrbit.MaxPitch, tilted.Pitch);
    }

    private static Frame RenderFrame(SkinBuilder skin, PlayerPose pose, SkinVariant variant = SkinVariant.Classic, CapeTexture? cape = null)
    {
        var renderer = new PlayerSkinRenderer(SkinTexture.FromRgba(skin.Width, skin.Height, skin.Pixels), variant)
        {
            // Flat lighting so colours can be compared exactly.
            Lighting = false,
            Cape = cape,
        };
        var bgra = new byte[W * H * 4];
        renderer.Render(pose, bgra, W, H);
        return new Frame(bgra);
    }

    private sealed class SkinBuilder
    {
        public SkinBuilder(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public SkinBuilder Fill(int x, int y, int w, int h, byte[] rgba)
        {
            for (var j = y; j < y + h; j++)
                for (var i = x; i < x + w; i++)
                    Array.Copy(rgba, 0, Pixels, (j * Width + i) * 4, 4);
            return this;
        }

        public SkinBuilder Clear(int x, int y, int w, int h) => Fill(x, y, w, h, new byte[4]);

        /// <summary>Make the whole second layer (hat, jacket, sleeves, pants) transparent.</summary>
        public SkinBuilder BaseOnly() => Clear(32, 0, 32, 16).Clear(0, 32, 64, 16).Clear(0, 48, 16, 16).Clear(48, 48, 16, 16);
    }

    private sealed class Frame(byte[] bgra)
    {
        public int Count(byte[] rgba, int fromRow, int toRow)
        {
            var n = 0;
            for (var y = fromRow; y < toRow; y++)
                for (var x = 0; x < W; x++)
                {
                    var i = (y * W + x) * 4;
                    if (bgra[i + 3] == 255 && bgra[i + 2] == rgba[0] && bgra[i + 1] == rgba[1] && bgra[i] == rgba[2]) n++;
                }
            return n;
        }

        public bool RowIsContiguous(int y)
        {
            int first = -1, last = -1, opaque = 0;
            for (var x = 0; x < W; x++)
            {
                if (bgra[(y * W + x) * 4 + 3] < 128) continue;
                if (first < 0) first = x;
                last = x;
                opaque++;
            }
            return first < 0 || opaque == last - first + 1;
        }

        public int OpaqueWidth(int y)
        {
            var n = 0;
            for (var x = 0; x < W; x++)
                if (bgra[(y * W + x) * 4 + 3] >= 128) n++;
            return n;
        }
    }
}
