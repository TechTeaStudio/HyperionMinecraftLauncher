using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Rendering;

/// <summary>
/// A player skin as straight (non-premultiplied) RGBA pixels in the modern square layout.
/// Legacy 64x32 skins are converted on load the way Minecraft does it: the right arm and leg
/// are mirrored into the empty left-limb slots and a fully opaque hat layer is made
/// transparent. HD skins (128x128, 256x256, ...) keep their resolution; texture coordinates
/// in the 64x64 layout are multiplied by <see cref="Scale"/>.
/// </summary>
public sealed class SkinTexture
{
    private readonly byte[] _rgba;

    private SkinTexture(byte[] rgba, int size)
    {
        _rgba = rgba;
        Width = size;
        Height = size;
        Scale = size / 64;
    }

    /// <summary>Width in pixels (64 for a standard skin).</summary>
    public int Width { get; }

    /// <summary>Height in pixels; always equal to <see cref="Width"/> after loading.</summary>
    public int Height { get; }

    /// <summary>Texels per unit of the 64x64 layout (1 for standard skins, 2 for 128x128, ...).</summary>
    public int Scale { get; }

    /// <summary>
    /// Load a skin from straight RGBA bytes. Accepts square skins and legacy half-height skins
    /// whose width is a multiple of 64.
    /// </summary>
    public static SkinTexture FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var legacy = height * 2 == width;
        if (width < 64 || width % 64 != 0 || (height != width && !legacy))
            throw new ArgumentException($"Unsupported skin size {width}x{height}; expected 64x64, 64x32 or an HD multiple.", nameof(width));
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(rgba));

        var square = new byte[width * width * 4];
        rgba[..(width * height * 4)].CopyTo(square);
        var texture = new SkinTexture(square, width);
        if (legacy) texture.ConvertLegacy();
        return texture;
    }

    /// <summary>The RGBA bytes of one pixel.</summary>
    public ReadOnlySpan<byte> GetRgba(int x, int y) => _rgba.AsSpan((y * Width + x) * 4, 4);

    internal byte[] Pixels => _rgba;

    // Same copies as Minecraft's SkinTextureDownloader.processLegacySkin, in 64x64-layout units.
    private void ConvertLegacy()
    {
        CopyMirrored(4, 16, 16, 32, 4, 4);
        CopyMirrored(8, 16, 16, 32, 4, 4);
        CopyMirrored(0, 20, 24, 32, 4, 12);
        CopyMirrored(4, 20, 16, 32, 4, 12);
        CopyMirrored(8, 20, 8, 32, 4, 12);
        CopyMirrored(12, 20, 16, 32, 4, 12);
        CopyMirrored(44, 16, -8, 32, 4, 4);
        CopyMirrored(48, 16, -8, 32, 4, 4);
        CopyMirrored(40, 20, 0, 32, 4, 12);
        CopyMirrored(44, 20, -8, 32, 4, 12);
        CopyMirrored(48, 20, -16, 32, 4, 12);
        CopyMirrored(52, 20, -8, 32, 4, 12);
        ClearHatIfFullyOpaque();
    }

    private void CopyMirrored(int fromX, int fromY, int deltaX, int deltaY, int width, int height)
    {
        var k = Scale;
        for (var y = 0; y < height * k; y++)
            for (var x = 0; x < width * k; x++)
            {
                var src = ((fromY * k + y) * Width + fromX * k + x) * 4;
                var dstX = (fromX + deltaX) * k + (width * k - 1 - x);
                var dst = (((fromY + deltaY) * k + y) * Width + dstX) * 4;
                Array.Copy(_rgba, src, _rgba, dst, 4);
            }
    }

    // "Notch transparency hack": old skins often painted the hat area solid. If no pixel of the
    // hat layer is transparent, treat the whole layer as absent.
    private void ClearHatIfFullyOpaque()
    {
        var k = Scale;
        for (var y = 0; y < 16 * k; y++)
            for (var x = 32 * k; x < 64 * k; x++)
                if (_rgba[(y * Width + x) * 4 + 3] < 128) return;

        for (var y = 0; y < 16 * k; y++)
            for (var x = 32 * k; x < 64 * k; x++)
                _rgba[(y * Width + x) * 4 + 3] = 0;
    }
}
