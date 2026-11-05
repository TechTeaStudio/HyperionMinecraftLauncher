using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

/// <summary>The cape picker's icons: the outer side of the cape, cut from its texture.</summary>
public class CapeIconsTests
{
    [Theory]
    [InlineData(64, 32)]   // current layout
    [InlineData(22, 17)]   // old capes, same coordinates
    [InlineData(128, 64)]  // HD cape, everything doubled
    public void RenderFront_CutsTheOuterSideOfTheCape(int width, int height)
    {
        var png = Cape(width, height);

        using var icon = CapeIcons.RenderFront(png);

        Assert.NotNull(icon);
        Assert.Equal(CapeIcons.IconWidth, icon!.Width);
        Assert.Equal(CapeIcons.IconHeight, icon.Height);
        for (var y = 0; y < icon.Height; y++)
            for (var x = 0; x < icon.Width; x++)
                Assert.Equal(SKColors.Red, icon.GetPixel(x, y));
    }

    [Fact]
    public void RenderFront_KeepsTheCapePattern()
    {
        // A cape whose outer side has a white top row: the icon's top rows are white too.
        var png = Cape(64, 32, paintTopRowWhite: true);

        using var icon = CapeIcons.RenderFront(png);

        var rowsPerTexel = CapeIcons.IconHeight / 16;
        Assert.Equal(SKColors.White, icon!.GetPixel(0, 0));
        Assert.Equal(SKColors.White, icon.GetPixel(CapeIcons.IconWidth - 1, rowsPerTexel - 1));
        Assert.Equal(SKColors.Red, icon.GetPixel(0, rowsPerTexel));
    }

    [Fact]
    public void RenderFront_TooSmallOrBrokenPng_GivesNoIcon()
    {
        Assert.Null(CapeIcons.RenderFront(null));
        Assert.Null(CapeIcons.RenderFront(new byte[] { 1, 2, 3 }));
        Assert.Null(CapeIcons.RenderFront(Cape(16, 16)));
    }

    // Blue everywhere except the outer side, at (1,1) 10x16 in 64x32 units, which is red.
    private static byte[] Cape(int width, int height, bool paintTopRowWhite = false)
    {
        var scale = width >= 64 ? width / 64 : 1;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Blue);
        for (var y = 1 * scale; y < 17 * scale && y < height; y++)
            for (var x = 1 * scale; x < 11 * scale && x < width; x++)
                bitmap.SetPixel(x, y, paintTopRowWhite && y < 2 * scale ? SKColors.White : SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
