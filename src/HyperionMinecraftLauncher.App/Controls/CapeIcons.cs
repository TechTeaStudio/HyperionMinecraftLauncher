using System;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>Cuts a small icon (the outer side of the cape) out of a cape texture.</summary>
public static class CapeIcons
{
    /// <summary>Icon width: the cape's 10 texels, 4 pixels each.</summary>
    public const int IconWidth = 40;

    /// <summary>Icon height: the cape's 16 texels, 4 pixels each.</summary>
    public const int IconHeight = 64;

    /// <summary>
    /// The outer side of the cape, at (1,1) 10x16 in the 64x32 layout (old 22x17 capes use the
    /// same spot, HD capes scale it), resampled pixel-sharp to <see cref="IconWidth"/> x
    /// <see cref="IconHeight"/>. Null when the PNG doesn't decode or is too small for a cape.
    /// The caller owns the returned bitmap.
    /// </summary>
    public static SKBitmap? RenderFront(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        try
        {
            using var decoded = SKBitmap.Decode(png);
            if (decoded is null || decoded.Width < 22 || decoded.Height < 17) return null;
            using var cape = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (!decoded.CopyTo(cape, SKColorType.Rgba8888)) return null;

            var scale = decoded.Width >= 64 ? decoded.Width / 64 : 1;
            var icon = new SKBitmap(new SKImageInfo(IconWidth, IconHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < IconHeight; y++)
            {
                var sy = Math.Min(scale + y * 16 * scale / IconHeight, cape.Height - 1);
                for (var x = 0; x < IconWidth; x++)
                {
                    var sx = Math.Min(scale + x * 10 * scale / IconWidth, cape.Width - 1);
                    icon.SetPixel(x, y, cape.GetPixel(sx, sy));
                }
            }
            return icon;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
