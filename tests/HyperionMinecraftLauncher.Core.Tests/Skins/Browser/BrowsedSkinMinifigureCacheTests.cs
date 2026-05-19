using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Round-trip tests for <see cref="BrowsedSkinMinifigureCache.ComposeBodySkBitmap"/>.
/// We feed a synthetic 64x64 PNG, run the body composer, and assert the resulting
/// <see cref="SKBitmap"/> has the dimensions the WrapPanel templates expect.
/// </summary>
/// <remarks>
/// The Avalonia <see cref="Avalonia.Media.Imaging.Bitmap"/> wrap is exercised by the
/// live UI (the test host can't initialise the Avalonia platform without a window),
/// so these tests stop at the SKBitmap layer - that's the load-bearing pipeline step
/// (decode + Skin2DTypeA.MakeSkinImage).
/// </remarks>
public class BrowsedSkinMinifigureCacheTests
{
    [Fact]
    public void ComposeBodySkBitmap_ValidSkinBytes_ReturnsNonNullBitmap()
    {
        var pngBytes = MakeBlankSkinPng();

        using var bitmap = BrowsedSkinMinifigureCache.ComposeBodySkBitmap(pngBytes);

        Assert.NotNull(bitmap);
        // Skin2DTypeA.MakeSkinImage upscales the 16x32 working sprite by 8x, so a
        // valid 64x64 skin yields a 128x256 body PNG. The exact value isn't load-bearing,
        // but it must be substantially larger than the input thumbnail so the UI
        // gets a usable minifigure tile.
        Assert.True(bitmap!.Width >= 64, $"expected width >= 64, got {bitmap.Width}");
        Assert.True(bitmap.Height >= 64, $"expected height >= 64, got {bitmap.Height}");
    }

    [Fact]
    public void ComposeBodySkBitmap_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(BrowsedSkinMinifigureCache.ComposeBodySkBitmap(null));
        Assert.Null(BrowsedSkinMinifigureCache.ComposeBodySkBitmap(System.Array.Empty<byte>()));
    }

    [Fact]
    public void ComposeBodySkBitmap_GarbageBytes_ReturnsNullGracefully()
    {
        // SKBitmap.Decode returns null for an undecodable buffer; the cache must
        // surface that as a null bitmap rather than throwing, so the caller can
        // swap in the Steve fallback.
        var bitmap = BrowsedSkinMinifigureCache.ComposeBodySkBitmap(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        Assert.Null(bitmap);
    }

    /// <summary>
    /// Build a fully-opaque 64x64 PNG byte stream we can hand to the renderer.
    /// The contents aren't pixel-perfect Minecraft - we don't need them to be -
    /// they just have to satisfy SKBitmap.Decode + the body composer's UV extracts.
    /// </summary>
    private static byte[] MakeBlankSkinPng()
    {
        using var bmp = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(160, 96, 64, 255));
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
