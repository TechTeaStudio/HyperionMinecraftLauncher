using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>A skin the player just uploaded to their Mojang profile.</summary>
public sealed class SkinAppliedEventArgs : EventArgs
{
    public SkinAppliedEventArgs(byte[] pngBytes, SkinVariant variant)
    {
        PngBytes = pngBytes;
        Variant = variant;
    }

    /// <summary>The uploaded skin PNG.</summary>
    public byte[] PngBytes { get; }

    /// <summary>Arm model it was uploaded with.</summary>
    public SkinVariant Variant { get; }
}
