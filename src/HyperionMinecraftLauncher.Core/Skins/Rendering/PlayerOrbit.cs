using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

/// <summary>Drag-to-rotate math for the skin viewer.</summary>
public static class PlayerOrbit
{
    /// <summary>Rotation per pixel of pointer movement.</summary>
    public const float DegreesPerPixel = 0.5f;

    /// <summary>Lowest camera tilt (looking up at the player).</summary>
    public const float MinPitch = -30f;

    /// <summary>Highest camera tilt (looking down at the player).</summary>
    public const float MaxPitch = 60f;

    /// <summary>
    /// Apply a pointer drag: horizontal movement turns the player (yaw wraps to (-180, 180]),
    /// dragging down tilts the camera over the top of the player (pitch is clamped).
    /// </summary>
    public static (float Yaw, float Pitch) Drag(float yawDegrees, float pitchDegrees, double dx, double dy)
    {
        var yaw = Wrap(yawDegrees + (float)dx * DegreesPerPixel);
        var pitch = Math.Clamp(pitchDegrees + (float)dy * DegreesPerPixel, MinPitch, MaxPitch);
        return (yaw, pitch);
    }

    private static float Wrap(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        else if (degrees <= -180f) degrees += 360f;
        return degrees;
    }
}
