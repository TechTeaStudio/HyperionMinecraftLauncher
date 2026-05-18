using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Pure, allocation-free rotation-angle arithmetic for the Skins-page 3D viewer.
/// Pulled out of <see cref="SkinPreview"/> so it can be exercised in unit tests
/// without an Avalonia / SkiaSharp context.
/// </summary>
/// <remarks>
/// <para>
/// Coloryr's <c>Skin3DHeadTypeB.MakeHeadImage(skin, x, y)</c> reads <c>x</c> as the
/// X-axis rotation (pitch, head tilt forward / back) and <c>y</c> as the Y-axis
/// rotation (yaw, head turn left / right). The viewer accumulates pointer-move
/// deltas in <see cref="AccumulateStep"/>: horizontal drag updates yaw, vertical
/// drag updates pitch, with yaw wrapped to [0, 360) and pitch clamped so the user
/// cannot flip the camera through the ground plane.
/// </para>
/// <para>
/// <see cref="IsBackFacing"/> picks the right 2D body sprite to show alongside the
/// rotating 3D head. Because the camera at yaw=90 / 270 sees the player face-on,
/// "back-facing" means yaw is closer to 0 / 180.
/// </para>
/// </remarks>
public static class SkinRotation
{
    /// <summary>Minimum pitch in degrees. 0 would flatten the head into the floor.</summary>
    public const int MinPitch = 5;

    /// <summary>Maximum pitch in degrees. 180 puts the camera under the model.</summary>
    public const int MaxPitch = 175;

    /// <summary>One pointer-move step's (pitch, yaw) result.</summary>
    public readonly record struct Step(int Pitch, int Yaw);

    /// <summary>
    /// Add a pointer delta to the previous (<paramref name="prevPitch"/>,
    /// <paramref name="prevYaw"/>) and return the clamped / wrapped result.
    /// </summary>
    /// <param name="prevPitch">Previous pitch in degrees.</param>
    /// <param name="prevYaw">Previous yaw in degrees, expected to be in [0, 360).</param>
    /// <param name="dx">Horizontal pointer delta in pixels (1px = 1deg of yaw).</param>
    /// <param name="dy">Vertical pointer delta in pixels (1px = 1deg of pitch).</param>
    public static Step AccumulateStep(int prevPitch, int prevYaw, int dx, int dy)
    {
        var yaw = WrapDeg(prevYaw + dx);
        // Drag DOWN (positive dy) should tilt the camera lower onto the head, i.e.
        // decrease pitch. This matches the convention used by every commercial
        // 3D modeller for the orbit gizmo.
        var pitch = Math.Clamp(prevPitch - dy, MinPitch, MaxPitch);
        return new Step(pitch, yaw);
    }

    /// <summary>
    /// Wrap an arbitrary integer degree value to <c>[0, 360)</c>. Handles negative
    /// values without the C# <c>%</c> sign-preservation pitfall.
    /// </summary>
    public static int WrapDeg(int v) => ((v % 360) + 360) % 360;

    /// <summary>
    /// <c>true</c> when the camera at the given yaw is looking at the back of the
    /// model (so the 2D body sprite should show the back textures). The 3D head
    /// renderer's default translation places the camera at yaw=90 / 270 looking at
    /// the face, and at yaw=0 / 180 looking at the back of the head. We mark the
    /// 60 deg arcs centred on 0 / 180 as "back-facing"; the wider 120 deg arcs around
    /// 90 / 270 (where at least one eye is visible on the rotating head) keep the
    /// front sprite, so the body doesn't flicker as the user drags around the side.
    /// </summary>
    public static bool IsBackFacing(int yaw)
    {
        var y = WrapDeg(yaw);
        // Back-facing arcs: [330, 360) U [0, 30) and [150, 210).
        return y < 30 || (y >= 150 && y < 210) || y >= 330;
    }
}
