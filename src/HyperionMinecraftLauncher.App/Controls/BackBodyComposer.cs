using MinecraftSkinRender;
using MinecraftSkinRender.Image;
using SkiaSharp;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Composes a 2D back-view body sprite. <c>MinecraftSkinRender.Image</c> 1.2.0 ships
/// only a front-view <see cref="Skin2DTypeA"/>; this class mirrors that algorithm
/// using the standard Minecraft back-face UV regions so the Skins-page viewer can
/// flip the body when the user drags the 3D head past the side.
/// </summary>
/// <remarks>
/// <para>
/// Front-view layout (16x32, pre-scale, taken straight from <see cref="Skin2DTypeA"/>):
/// </para>
/// <code>
/// columns: arm | body | arm
///   right arm:  x=0  width=4   (UV 44,20)
///   body     :  x=4  width=8   (UV 20,20)
///   left arm :  x=12 width=4   (UV 36,52 new layout; mirror of right in old)
///   right leg:  x=4  width=4   (UV 4,20)
///   left leg :  x=8  width=4   (UV 20,52 new layout; mirror of right in old)
/// </code>
/// <para>
/// Back-view: the model rotates 180 deg around Y, so what was the model's right side
/// is now on the camera's right side. We swap the X destination columns for the arms
/// and legs, and use the BACK-face UV regions: each part's back face is shifted +8
/// for the body / right limbs (or to its own column for the new-layout left limbs).
/// </para>
/// </remarks>
internal static class BackBodyComposer
{
    private const int Scale = 8;

    public static SKBitmap MakeBackImage(SKBitmap image, SkinType? type = null)
    {
        var skintype = type ?? SkinTypeChecker.GetTextType(image);

        // 16x32 working layer at native resolution. Front view uses the same shape.
        using var image1 = new SKBitmap(16, 32);
        var working = new SKBitmap(16 * Scale, 32 * Scale);

        // --- HEAD (back face) ---
        // Standard UV: head back = (24, 8, 8x8). Hat overlay back = (56, 8, 8x8).
        ImageHelper.ExtractSubset(image1, image, 4, 0, 24, 8, 8, 8);
        ImageHelper.ExtractSubsetMix(image1, image, 4, 0, 56, 8, 8, 8);

        // --- BODY (back face) ---
        // Front UV (20, 20) -> back UV (32, 20). Jacket overlay (32, 36).
        ImageHelper.ExtractSubset(image1, image, 4, 8, 32, 20, 8, 12);
        if (skintype is SkinType.New or SkinType.NewSlim)
            ImageHelper.ExtractSubsetMix(image1, image, 4, 8, 32, 36, 8, 12);

        // --- RIGHT ARM (model's right; appears on CAMERA RIGHT when viewed from back) ---
        // Front UV (44, 20) -> back UV (52, 20). Slim arms = 3 wide.
        int rArmDx = 12, rArmW = (skintype == SkinType.NewSlim) ? 3 : 4;
        ImageHelper.ExtractSubset(image1, image, rArmDx, 8, 52, 20, rArmW, 12);
        if (skintype != SkinType.Old)
            ImageHelper.ExtractSubsetMix(image1, image, rArmDx, 8, 52, 36, rArmW, 12);

        // --- LEFT ARM (model's left; appears on CAMERA LEFT when viewed from back) ---
        // Old layout: legacy skins have no left-arm UVs, so mirror the right arm back face.
        // New layout: dedicated UV (36, 52) front, (44, 52) back; overlay at (60, 52).
        int lArmDx = 0, lArmW = (skintype == SkinType.NewSlim) ? 3 : 4;
        if (skintype is SkinType.Old)
        {
            // Mirror the right-arm back UV (52, 20) onto the left arm column.
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 12; j++)
                    image1.SetPixel(3 - i, j + 8, image.GetPixel(i + 52, j + 20));
        }
        else
        {
            ImageHelper.ExtractSubset(image1, image, lArmDx, 8, 44, 52, lArmW, 12);
            ImageHelper.ExtractSubsetMix(image1, image, lArmDx, 8, 60, 52, lArmW, 12);
        }

        // --- RIGHT LEG (model's right; on CAMERA RIGHT in back view) ---
        // Front UV (4, 20) -> back UV (12, 20). Overlay (12, 36).
        ImageHelper.ExtractSubset(image1, image, 8, 20, 12, 20, 4, 12);
        if (skintype is SkinType.New or SkinType.NewSlim)
            ImageHelper.ExtractSubsetMix(image1, image, 8, 20, 12, 36, 4, 12);

        // --- LEFT LEG (model's left; on CAMERA LEFT in back view) ---
        // Old layout: mirror the right leg's back UV.
        // New layout: dedicated back UV (28, 52); overlay (12, 52).
        if (skintype is SkinType.Old)
        {
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 12; j++)
                    image1.SetPixel(7 - i, j + 20, image.GetPixel(i + 12, j + 20));
        }
        else
        {
            ImageHelper.ExtractSubset(image1, image, 4, 20, 28, 52, 4, 12);
            ImageHelper.ExtractSubsetMix(image1, image, 4, 20, 12, 52, 4, 12);
        }

        // Nearest-neighbour upscale to 8x, matching Skin2DTypeA.
        for (int i = 0; i < 16 * Scale; i++)
        {
            for (int j = 0; j < 32 * Scale; j++)
                working.SetPixel(i, j, image1.GetPixel(i / Scale, j / Scale));
        }

        var result = working.Copy();
        working.Dispose();
        return result;
    }
}
