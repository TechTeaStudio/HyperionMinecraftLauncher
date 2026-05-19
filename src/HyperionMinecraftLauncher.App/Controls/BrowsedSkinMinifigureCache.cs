using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MinecraftSkinRender.Image;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Async cache that materialises an assembled-minifigure body sprite from a
/// <see cref="BrowsedSkin"/>: download the raw skin PNG, run
/// <see cref="Skin2DTypeA.MakeSkinImage(SKBitmap, MinecraftSkinRender.SkinType?)"/> against it,
/// and surface the result as an Avalonia <see cref="Bitmap"/> suitable for the
/// Skins-page card grid. Replaces the bare 64x64 sprite-sheet that earlier versions
/// showed inside each card.
/// </summary>
/// <remarks>
/// <para>
/// The cache is keyed by <see cref="BrowsedSkin.Id"/> so two cards pointing at the
/// same record only hit the network once. Failures (network down, decode error,
/// SkiaSharp throw) fall back to the bundled Steve full-skin asset so the layout
/// doesn't break and the user can still click the card.
/// </para>
/// <para>
/// Lifetime: process-wide singleton. The launcher's main window owns one instance;
/// tests can construct their own with a stub <see cref="ISkinBrowser"/>.
/// </para>
/// </remarks>
public sealed class BrowsedSkinMinifigureCache
{
    /// <summary>
    /// avares:// path to the bundled 64x64 Steve skin PNG. When a card fails to download
    /// (or the user runs the launcher fully offline), this is the placeholder rendered
    /// in its slot. The Avalonia asset loader resolves this on first use.
    /// </summary>
    public const string SteveFallbackAssetUri =
        "avares://HyperionMinecraftLauncher/Assets/Icons/MC/steve.png";

    private readonly ISkinBrowser _browser;
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _bodyCache =
        new(StringComparer.Ordinal);

    private Bitmap? _steveFallback;
    private readonly object _steveLock = new();

    /// <summary>Construct against a configured <see cref="ISkinBrowser"/>.</summary>
    public BrowsedSkinMinifigureCache(ISkinBrowser browser)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    /// <summary>
    /// Get (or asynchronously compute) the minifigure body sprite for <paramref name="skin"/>.
    /// The returned task is cached so concurrent callers share the same download. Never throws:
    /// failures resolve to <see cref="LoadSteveFallback"/>.
    /// </summary>
    public Task<Bitmap?> GetBodyImageAsync(BrowsedSkin skin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return _bodyCache.GetOrAdd(skin.Id, _ => BuildBodyAsync(skin, cancellationToken));
    }

    private async Task<Bitmap?> BuildBodyAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _browser.DownloadPngAsync(skin, cancellationToken).ConfigureAwait(false);
            var bitmap = RenderBodyFromPng(bytes);
            return bitmap ?? LoadSteveFallback();
        }
        catch (Exception)
        {
            // Any failure on the build path - DNS, 404, throttled CDN, decode error -
            // surfaces as the Steve placeholder so the user still sees a card-shaped
            // tile rather than an empty hole in the grid.
            return LoadSteveFallback();
        }
    }

    /// <summary>
    /// Decode a 64x64 skin PNG, run the front-facing 2D body composer, and re-encode
    /// to an Avalonia <see cref="Bitmap"/>. Public so tests can exercise the renderer
    /// without a network round-trip.
    /// </summary>
    /// <returns>
    /// The composed body sprite, or null when the PNG is empty / undecodable / the
    /// renderer throws. Callers swap a null result for the Steve fallback bitmap.
    /// </returns>
    public static Bitmap? RenderBodyFromPng(byte[]? pngBytes)
    {
        var sk = ComposeBodySkBitmap(pngBytes);
        if (sk is null) return null;
        try
        {
            return ToAvaloniaBitmap(sk);
        }
        finally
        {
            sk.Dispose();
        }
    }

    /// <summary>
    /// Decode and compose, but stop just before the Avalonia <see cref="Bitmap"/> wrap.
    /// Test seam: tests can verify the body-pixel pipeline without needing the Avalonia
    /// platform to be initialised.
    /// </summary>
    /// <returns>
    /// Owned <see cref="SKBitmap"/> (the caller must <see cref="SKBitmap.Dispose"/> it),
    /// or null when the PNG is empty / undecodable / the renderer throws.
    /// </returns>
    public static SKBitmap? ComposeBodySkBitmap(byte[]? pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0) return null;

        SKBitmap? sk = null;
        try
        {
            sk = SKBitmap.Decode(pngBytes);
            if (sk is null) return null;

            // Skin2DTypeA renders the full front-facing player silhouette - head, torso,
            // arms, legs - on a transparent background, exactly what the user described as
            // a "tiny assembled minifigure" in place of the raw sprite-sheet. We return
            // an owned SKBitmap so callers can convert to whatever sink they need.
            return Skin2DTypeA.MakeSkinImage(sk, null);
        }
        catch
        {
            return null;
        }
        finally
        {
            sk?.Dispose();
        }
    }

    /// <summary>
    /// Resolve and cache the bundled Steve skin as an Avalonia <see cref="Bitmap"/>.
    /// Loaded lazily on the first failure path so the asset loader isn't hit during
    /// the happy-path render.
    /// </summary>
    public Bitmap? LoadSteveFallback()
    {
        if (_steveFallback is not null) return _steveFallback;
        lock (_steveLock)
        {
            if (_steveFallback is not null) return _steveFallback;
            try
            {
                using var stream = AssetLoader.Open(new Uri(SteveFallbackAssetUri));
                using var sk = SKBitmap.Decode(stream);
                if (sk is null) return null;
                using var bodySk = Skin2DTypeA.MakeSkinImage(sk, null);
                _steveFallback = ToAvaloniaBitmap(bodySk);
            }
            catch
            {
                _steveFallback = null;
            }
            return _steveFallback;
        }
    }

    private static Bitmap? ToAvaloniaBitmap(SKBitmap? bitmap)
    {
        if (bitmap is null) return null;
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
