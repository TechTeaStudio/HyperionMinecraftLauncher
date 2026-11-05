using System;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>Compares skins by what they look like rather than by their PNG bytes.</summary>
public static class SkinPictures
{
    /// <summary>
    /// True when both PNGs hold the same picture. Mojang re-encodes uploaded skins, so a skin
    /// fetched back from it rarely matches the uploaded file byte for byte.
    /// </summary>
    public static bool Same(byte[] a, byte[] b)
    {
        if (a.AsSpan().SequenceEqual(b)) return true;
        var pixelsA = Pixels(a);
        var pixelsB = Pixels(b);
        return pixelsA is not null && pixelsB is not null && pixelsA.AsSpan().SequenceEqual(pixelsB);
    }

    // Width, height and RGBA rows of the decoded picture, or null when it doesn't decode.
    private static byte[]? Pixels(byte[] png)
    {
        try
        {
            using var decoded = SKBitmap.Decode(png);
            if (decoded is null) return null;
            using var rgba = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (!decoded.CopyTo(rgba, SKColorType.Rgba8888)) return null;
            var pixels = rgba.Bytes;
            var result = new byte[8 + pixels.Length];
            BitConverter.TryWriteBytes(result.AsSpan(0, 4), decoded.Width);
            BitConverter.TryWriteBytes(result.AsSpan(4, 4), decoded.Height);
            pixels.CopyTo(result, 8);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
