using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// <see cref="ISkinBrowser"/> backed by the public <c>https://api.mineskin.org/v2/skins</c>
/// REST endpoint. Replaces <see cref="NameMcSkinBrowser"/> in v0.32.3 because NameMC's
/// Cloudflare layer now serves a JavaScript challenge (<c>cf-mitigated=challenge</c>) to
/// every scripted request, so pure HTTP cannot reach the gallery without a real browser.
/// </summary>
/// <remarks>
/// <para>
/// MineSkin's v2 list endpoint returns clean JSON with no anti-bot wall: each entry has
/// <c>uuid</c>, <c>shortId</c> (8-char ID for the <c>https://minesk.in/{shortId}</c> page),
/// <c>name</c> (often <c>null</c>), <c>texture</c> (the 64-char hash on
/// <c>textures.minecraft.net/texture/{hash}</c>) and a <c>timestamp</c>. The PNG download
/// URL is the same CDN every Mojang account ships its active skin from.
/// </para>
/// <para>
/// MineSkin does <strong>not</strong> publish per-skin model (Classic vs Slim) on the
/// anonymous feed, so every browsed skin is surfaced as <see cref="SkinVariant.Classic"/>.
/// The user can still pick a slim variant from the dedicated Upload control if they need to.
/// </para>
/// <para>
/// MineSkin's anonymous tier ignores the <c>?name=</c> filter (it issues a warning and
/// returns the unfiltered feed). To make search useful without auth we fall back to a
/// client-side filter on the entry's <see cref="MineSkinEntry.Name"/> and
/// <see cref="MineSkinEntry.ShortId"/> substring after pulling a larger page from the API.
/// </para>
/// </remarks>
public sealed class MineSkinBrowser : ISkinBrowser
{
    /// <summary>v2 listing endpoint stem (paginated, size-capped at 1000 by the server).</summary>
    public const string ListUrlStem = "https://api.mineskin.org/v2/skins";

    /// <summary>Direct CDN for raw skin PNGs.</summary>
    public const string TextureCdn = "https://textures.minecraft.net/texture/";

    /// <summary>Public web page for a skin, useful for "Open in browser" tooltips.</summary>
    public const string WebPageStem = "https://minesk.in/";

    /// <summary>How many extra entries to pull when the caller asks for a client-side search.</summary>
    /// <remarks>
    /// MineSkin v2 ignores the <c>?name=</c> filter without an API key, so we pull a larger
    /// page (up to <c>limit * 8</c>, capped at 200) and filter locally. 200 is the smallest
    /// page that gives a search a fighting chance without slowing the UI.
    /// </remarks>
    public const int SearchPageMultiplier = 8;

    /// <summary>Hard upper bound on a single API page we request (server caps at 1000).</summary>
    public const int MaxApiPageSize = 200;

    private readonly HttpClient _http;

    /// <summary>Construct against an arbitrary <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// MineSkin's API has no anti-bot wall, so the launcher's default headers from the
    /// F2 work (Chrome-shaped UA + Accept-Language + Sec-Fetch-*) are accepted unchanged.
    /// The launcher reuses the same <c>HttpClient</c> instance that previously fed the
    /// NameMC browser. We don't reconfigure anything here.
    /// </remarks>
    public MineSkinBrowser(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit > 0 ? limit : 60, 1, MaxApiPageSize);
        var url = $"{ListUrlStem}?size={pageSize}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseList(json, limit, query: null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q))
            return await ListTrendingAsync(limit, cancellationToken).ConfigureAwait(false);

        // Pull a larger page so the client-side filter has more to chew on. We still hand
        // MineSkin the name filter even though the anonymous tier ignores it - the moment
        // the user pastes an API key (a future onboarding affordance) the server-side
        // filter takes effect.
        var pageSize = Math.Clamp(limit > 0 ? limit * SearchPageMultiplier : MaxApiPageSize, 1, MaxApiPageSize);
        var url = $"{ListUrlStem}?size={pageSize}&name={Uri.EscapeDataString(q)}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseList(json, limit, query: q);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (string.IsNullOrWhiteSpace(skin.PngDownloadUrl))
            throw new ArgumentException("BrowsedSkin.PngDownloadUrl is empty.", nameof(skin));

        using var response = await _http
            .GetAsync(skin.PngDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse a MineSkin v2 <c>/v2/skins</c> JSON response into <see cref="BrowsedSkin"/> records.
    /// Public so tests can exercise the parser from a JSON string without HTTP.
    /// </summary>
    /// <param name="json">Raw JSON returned by the list endpoint.</param>
    /// <param name="limit">Maximum number of skins to return; non-positive means "no cap".</param>
    /// <param name="query">
    /// Optional case-insensitive substring filter applied to each entry's <c>name</c> and
    /// <c>shortId</c>. <c>null</c> means "no filter".
    /// </param>
    /// <remarks>
    /// Failures inside a single entry (missing texture hash, malformed JSON shape) are
    /// silently skipped so a partial response still yields the rest of the feed.
    /// </remarks>
    public static IReadOnlyList<BrowsedSkin> ParseList(string json, int limit, string? query)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<BrowsedSkin>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Array.Empty<BrowsedSkin>();
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("skins", out var skinsEl) ||
                skinsEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BrowsedSkin>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<BrowsedSkin>();

            foreach (var entry in skinsEl.EnumerateArray())
            {
                if (limit > 0 && results.Count >= limit) break;
                var parsed = TryParseEntry(entry, query);
                if (parsed is null) continue;
                if (!seen.Add(parsed.Id)) continue;
                results.Add(parsed);
            }

            return results;
        }
    }

    /// <summary>
    /// Try to map a single JSON entry from <c>skins[]</c> to a <see cref="BrowsedSkin"/>.
    /// Returns null when the entry is missing the texture hash (the only field we can't
    /// fall back on) or fails the optional <paramref name="query"/> filter.
    /// </summary>
    public static BrowsedSkin? TryParseEntry(JsonElement entry, string? query)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;

        var texture = GetStringOrNull(entry, "texture");
        if (string.IsNullOrEmpty(texture)) return null;

        var uuid = GetStringOrNull(entry, "uuid") ?? texture;
        var shortId = GetStringOrNull(entry, "shortId") ?? string.Empty;
        var name = GetStringOrNull(entry, "name");

        // Client-side filter: substring match against name + shortId. Case-insensitive.
        if (!string.IsNullOrEmpty(query))
        {
            var match = (name is not null && name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(shortId) && shortId.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (!match) return null;
        }

        var sourceUrl = string.IsNullOrEmpty(shortId)
            ? $"{WebPageStem}{uuid}"
            : $"{WebPageStem}{shortId}";

        // MineSkin's anonymous feed gives no thumbnail URL, so we point the gallery image
        // directly at the raw 64x64 PNG on textures.minecraft.net. Avalonia's AsyncImageLoader
        // scales it up with bitmap interpolation off (set on the consuming Image), giving
        // the pixel-art look the rest of the skins UI already uses.
        var pngUrl = TextureCdn + texture;
        var thumbnail = pngUrl;

        // Use the skin name when present; otherwise fall back to the shortId so the user
        // has *something* to identify the card by in the selected-card panel.
        var uploader = !string.IsNullOrWhiteSpace(name)
            ? name
            : (!string.IsNullOrEmpty(shortId) ? shortId : null);

        return new BrowsedSkin(
            Id: uuid,
            SourceUrl: sourceUrl,
            ThumbnailUrl: thumbnail,
            PngDownloadUrl: pngUrl,
            Variant: SkinVariant.Classic,
            UploaderName: uploader,
            Tags: Array.Empty<string>(),
            Likes: 0);
    }

    private static string? GetStringOrNull(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
