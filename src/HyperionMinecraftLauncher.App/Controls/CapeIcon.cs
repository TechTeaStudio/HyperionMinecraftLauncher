using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Small picture of a cape (its outer side) for the cape picker. Set <see cref="Url"/> to the
/// cape texture; the texture is downloaded once, kept in the launcher cache (texture URLs
/// never change content) and shared by every icon showing the same cape.
/// </summary>
public sealed class CapeIcon : Image
{
    /// <summary>Texture URL of the cape, as Mojang's profile lists it.</summary>
    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<CapeIcon, string?>(nameof(Url));

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Icons = new(StringComparer.Ordinal);
    private static readonly Lazy<FileCache> Cache = new(() => new FileCache());

    /// <inheritdoc cref="UrlProperty"/>
    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Image);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UrlProperty)
            _ = ShowAsync(change.GetNewValue<string?>());
    }

    private async Task ShowAsync(string? url)
    {
        Source = null;
        if (string.IsNullOrEmpty(url)) return;
        var task = Icons.GetOrAdd(url, BuildAsync);
        var icon = await task;
        // A failed download is retried next time instead of staying blank for the session.
        if (icon is null) Icons.TryRemove(new KeyValuePair<string, Task<Bitmap?>>(url, task));
        if (Url == url) Source = icon;
    }

    private static async Task<Bitmap?> BuildAsync(string url)
    {
        try
        {
            var key = $"capes/{CacheName(url)}.png";
            var png = await Cache.Value.TryReadAsync(key, maxAge: null, CancellationToken.None).ConfigureAwait(false);
            if (png is null)
            {
                png = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                try { await Cache.Value.WriteAsync(key, png, CancellationToken.None).ConfigureAwait(false); }
                catch (IOException) { }
            }

            using var front = CapeIcons.RenderFront(png);
            if (front is null) return null;
            using var encoded = front.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(encoded.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Texture URLs end in the content hash; anything else is hashed so it is a safe file name.
    private static string CacheName(string url)
    {
        var last = url.TrimEnd('/').Split('/').Last();
        return last.Length is > 0 and <= 128 && last.All(char.IsAsciiHexDigit)
            ? last.ToLowerInvariant()
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
    }
}
