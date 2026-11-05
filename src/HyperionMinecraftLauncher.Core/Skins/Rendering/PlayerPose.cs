using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

/// <summary>
/// Camera angle and limb swing for one rendered frame of the player model.
/// </summary>
/// <param name="YawDegrees">Turn around the vertical axis; 0 shows the front, 180 the back.</param>
/// <param name="PitchDegrees">Camera tilt; positive looks down at the player from above.</param>
/// <param name="LimbSwing">Walk swing in radians; arms and legs on each side move in opposite phase.</param>
public readonly record struct PlayerPose(float YawDegrees, float PitchDegrees, float LimbSwing = 0f)
{
    /// <summary>Length of one full stride (left and right step), in seconds.</summary>
    public const double WalkCycleSeconds = 1.1;

    /// <summary>Largest leg swing of the walk, in radians (about 40 degrees).</summary>
    public const float MaxWalkSwing = 0.7f;

    /// <summary>The walking pose at <paramref name="seconds"/> into the animation.</summary>
    public static PlayerPose Walking(float yawDegrees, float pitchDegrees, double seconds)
    {
        var phase = 2 * Math.PI * seconds / WalkCycleSeconds;
        return new PlayerPose(yawDegrees, pitchDegrees, (float)(MaxWalkSwing * Math.Sin(phase)));
    }
}
