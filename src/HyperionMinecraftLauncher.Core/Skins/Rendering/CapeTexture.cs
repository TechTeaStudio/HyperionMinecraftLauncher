using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

/// <summary>
/// A cape as straight RGBA pixels. The net is the 64x32 layout (outer side at (1,1) 10x16,
/// inner side at (12,1)); old 22x17 capes use the same coordinates, and HD capes scale them
/// by <see cref="Scale"/>.
/// </summary>
public sealed class CapeTexture
{
    private CapeTexture(byte[] rgba, int width, int height)
    {
        Pixels = rgba;
        Width = width;
        Height = height;
        Scale = width >= 64 ? width / 64 : 1;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Texels per unit of the 64x32 layout.</summary>
    public int Scale { get; }

    internal byte[] Pixels { get; }

    /// <summary>Load a cape from straight RGBA bytes; it must be at least 22x17.</summary>
    public static CapeTexture FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width < 22 || height < 17)
            throw new ArgumentException($"Unsupported cape size {width}x{height}; expected at least 22x17.", nameof(width));
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(rgba));
        return new CapeTexture(rgba[..(width * height * 4)].ToArray(), width, height);
    }
}
