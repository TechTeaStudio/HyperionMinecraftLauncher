using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

/// <summary>
/// Pure-arithmetic tests for the rotation accumulation logic used by the Skins-page
/// 3D viewer. The actual rendering (SkiaSharp + Avalonia Bitmap pipeline) is integration
/// territory; here we just pin the angle math so future drag-handler refactors do not
/// silently regress yaw wrapping or pitch clamping.
/// </summary>
public class SkinPreviewRotationTests
{
    [Fact]
    public void WrapDeg_NegativeAngle_WrapsIntoZeroTo360Range()
    {
        Assert.Equal(355, SkinRotation.WrapDeg(-5));
        Assert.Equal(180, SkinRotation.WrapDeg(-180));
        Assert.Equal(0, SkinRotation.WrapDeg(-360));
        Assert.Equal(359, SkinRotation.WrapDeg(-721));
    }

    [Fact]
    public void WrapDeg_LargePositiveAngle_WrapsIntoZeroTo360Range()
    {
        Assert.Equal(0, SkinRotation.WrapDeg(360));
        Assert.Equal(15, SkinRotation.WrapDeg(375));
        Assert.Equal(1, SkinRotation.WrapDeg(721));
    }

    [Fact]
    public void AccumulateStep_HorizontalDragIncreasesYawAndWraps()
    {
        // Default viewer state: pitch=15, yaw=65. Drag right by 350 px should wrap.
        var step = SkinRotation.AccumulateStep(prevPitch: 15, prevYaw: 65, dx: 350, dy: 0);
        Assert.Equal(15, step.Pitch);                 // pitch untouched
        Assert.Equal(55, step.Yaw);                   // 65 + 350 = 415, wraps to 55
    }

    [Fact]
    public void AccumulateStep_VerticalDragDecreasesPitchAndClamps()
    {
        // Dragging the head DOWN (positive dy) should tilt camera lower onto the head,
        // i.e. decrease pitch. Past MinPitch the clamp must kick in so the camera does
        // not flip through the floor.
        var step = SkinRotation.AccumulateStep(prevPitch: 30, prevYaw: 90, dx: 0, dy: 200);
        Assert.Equal(SkinRotation.MinPitch, step.Pitch);
        Assert.Equal(90, step.Yaw);                   // yaw untouched
    }

    [Fact]
    public void AccumulateStep_VerticalDragUpClampsToMaxPitch()
    {
        var step = SkinRotation.AccumulateStep(prevPitch: 170, prevYaw: 0, dx: 0, dy: -100);
        Assert.Equal(SkinRotation.MaxPitch, step.Pitch);
    }

    [Fact]
    public void AccumulateStep_SequenceMatchesDirectSum()
    {
        // Simulating the actual pointer-move loop: each PointerMoved event hands the
        // PREVIOUS yaw + the latest pixel delta into AccumulateStep. After three drags
        // totaling +120 yaw and -30 pitch, we should end up at (65 + 120, 15 - (-30))
        // = (yaw 185, pitch 45).
        var s1 = SkinRotation.AccumulateStep(15, 65, dx: 30, dy: -10);
        var s2 = SkinRotation.AccumulateStep(s1.Pitch, s1.Yaw, dx: 50, dy: -15);
        var s3 = SkinRotation.AccumulateStep(s2.Pitch, s2.Yaw, dx: 40, dy: -5);

        Assert.Equal(185, s3.Yaw);
        Assert.Equal(45, s3.Pitch);
    }

    [Theory]
    [InlineData(0, true)]    // looking straight at the back of the head
    [InlineData(15, true)]   // still mostly seeing the back
    [InlineData(29, true)]   // last yaw before the front sprite kicks in
    [InlineData(30, false)]  // boundary - front sprite shows from here
    [InlineData(90, false)]  // face-on; obvious front
    [InlineData(150, true)]  // turned around past the side -> back again
    [InlineData(200, true)]
    [InlineData(210, false)] // boundary on the other 90 deg pole
    [InlineData(270, false)] // the other face-on view
    [InlineData(330, true)]  // tail of the circle
    [InlineData(359, true)]
    public void IsBackFacing_CorrectAtBoundaries(int yaw, bool expected)
    {
        Assert.Equal(expected, SkinRotation.IsBackFacing(yaw));
    }

    [Fact]
    public void IsBackFacing_AcceptsNegativeYawByWrapping()
    {
        // -10 deg should behave like 350 deg (back-facing tail of circle).
        Assert.True(SkinRotation.IsBackFacing(-10));
        // -100 deg = 260 deg, which is on the front-face arc.
        Assert.False(SkinRotation.IsBackFacing(-100));
    }
}
