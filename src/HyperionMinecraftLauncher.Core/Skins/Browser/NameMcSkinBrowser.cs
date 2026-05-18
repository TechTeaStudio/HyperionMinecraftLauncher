using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// <see cref="ISkinBrowser"/> backed by public pages on <c>https://namemc.com</c>.
/// </summary>
/// <remarks>
/// <para>
/// NameMC publishes <strong>no official API</strong>. This implementation parses
/// the HTML of the public trending and search pages with HtmlAgilityPack and is
/// best-effort: structural changes upstream can require selector tweaks. A failure
/// to parse a single card is swallowed; only a wholesale fetch failure surfaces
/// as an exception.
/// </para>
/// <para>
/// The skin "hash" appears in every card href as <c>/skin/{hash}</c>. Combined with
/// the documented NameMC asset URLs:
/// <list type="bullet">
///   <item><description><c>https://textures.minecraft.net/texture/{hash}</c> -
///   raw 64x64 PNG (the same CDN every Mojang account ships its active skin from).</description></item>
///   <item><description><c>https://s.namemc.com/3d/skin/body.png?id={hash}&amp;model={classic|slim}</c> -
///   pre-rendered body thumbnail (used as the gallery image).</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class NameMcSkinBrowser : ISkinBrowser
{
    /// <summary>Public landing-page URL the parser targets for "Show trending".</summary>
    public const string TrendingUrl = "https://namemc.com/minecraft-skins/trending/top";

    /// <summary>Public search URL stem; the query is appended via <c>?q=...</c>.</summary>
    public const string SearchUrlStem = "https://namemc.com/minecraft-skins";

    /// <summary>
    /// User-Agent string the browser identifies as. Bumped every release alongside
    /// the launcher version so an upstream block can be diagnosed from access logs.
    /// </summary>
    public const string UserAgent = "HyperionMinecraftLauncher/0.32.1 (contact@techteastudio.cc)";

    /// <summary>Direct CDN for raw skin PNGs (the same one Mojang's profile URLs serve from).</summary>
    public const string TextureCdn = "https://textures.minecraft.net/texture/";

    /// <summary>Pre-rendered body-thumbnail CDN. The model query param doubles as the variant hint.</summary>
    public const string ThumbCdn = "https://s.namemc.com/3d/skin/body.png";

    private readonly HttpClient _http;

    /// <summary>Construct against an arbitrary <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// The constructor ensures the standard NameMC User-Agent and an
    /// <c>Accept: text/html</c> default. Tests pass in a client wired to a mock
    /// <c>HttpMessageHandler</c> so the parser can be exercised without network.
    /// </remarks>
    public NameMcSkinBrowser(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken)
    {
        var html = await GetHtmlAsync(TrendingUrl, cancellationToken).ConfigureAwait(false);
        return ParseGallery(html, limit);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var q = query ?? string.Empty;
        var url = string.IsNullOrWhiteSpace(q)
            ? TrendingUrl
            : SearchUrlStem + "?q=" + Uri.EscapeDataString(q);
        var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseGallery(html, limit);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skin);
        var url = string.IsNullOrWhiteSpace(skin.PngDownloadUrl)
            ? throw new ArgumentException("BrowsedSkin.PngDownloadUrl is empty.", nameof(skin))
            : skin.PngDownloadUrl;
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse a NameMC gallery HTML page into a (capped) list of <see cref="BrowsedSkin"/>.
    /// Public so the parser tests can exercise it from a string fixture without HTTP.
    /// </summary>
    /// <param name="html">Raw HTML returned by the trending / search endpoint.</param>
    /// <param name="limit">Maximum number of skins to return; non-positive means "no cap".</param>
    /// <remarks>
    /// Failures inside a single card (missing href, malformed hash, etc.) are silently
    /// skipped so a partially-broken page still yields the rest of the gallery.
    /// </remarks>
    public static IReadOnlyList<BrowsedSkin> ParseGallery(string html, int limit)
    {
        if (string.IsNullOrEmpty(html)) return Array.Empty<BrowsedSkin>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Pick anchor nodes whose href starts with "/skin/" - the canonical card link.
        // We deliberately don't bind to a wrapper class name; the parent .card / .skin-card
        // class strings are the most fragile bits of the NameMC markup.
        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, '/skin/')]");
        if (anchors is null) return Array.Empty<BrowsedSkin>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<BrowsedSkin>();

        foreach (var a in anchors)
        {
            if (limit > 0 && results.Count >= limit) break;

            BrowsedSkin? parsed;
            try
            {
                parsed = TryParseCard(a);
            }
            catch
            {
                // Defensive: any one card going wrong should not poison the page.
                parsed = null;
            }

            if (parsed is null) continue;
            if (!seen.Add(parsed.Id)) continue; // de-dupe: trending pages often repeat the same card across grid breakpoints
            results.Add(parsed);
        }

        return results;
    }

    /// <summary>
    /// Parse one card anchor. Returns null when the href doesn't carry a valid hash
    /// (the card belongs to some other section, e.g. a category teaser).
    /// </summary>
    private static BrowsedSkin? TryParseCard(HtmlNode anchor)
    {
        var href = anchor.GetAttributeValue("href", null);
        if (string.IsNullOrEmpty(href)) return null;

        var hash = ExtractHashFromHref(href);
        if (hash is null) return null;

        // Walk up the DOM to find the enclosing card so we can pick uploader / likes /
        // tags from sibling nodes. The card wrapper varies (article, div), so we cap
        // the walk distance and accept either.
        var card = ClosestCard(anchor);

        var sourceUrl = AbsoluteSourceUrl(href);
        var variant = DetectVariant(anchor, card);
        var thumbnail = DetectThumbnail(anchor, hash, variant);

        var uploader = DetectUploader(card);
        var tags = DetectTags(card);
        var likes = DetectLikes(card);

        return new BrowsedSkin(
            Id: hash,
            SourceUrl: sourceUrl,
            ThumbnailUrl: thumbnail,
            PngDownloadUrl: TextureCdn + hash,
            Variant: variant,
            UploaderName: uploader,
            Tags: tags,
            Likes: likes);
    }

    /// <summary>
    /// Pull the skin hash out of a NameMC card href. NameMC URLs come in two shapes:
    /// <list type="bullet">
    ///   <item><description><c>/skin/{hash}</c> - canonical link</description></item>
    ///   <item><description><c>https://namemc.com/skin/{hash}</c> - absolute</description></item>
    /// </list>
    /// Returns null when no hash is present.
    /// </summary>
    public static string? ExtractHashFromHref(string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        const string Marker = "/skin/";
        var idx = href.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = href[(idx + Marker.Length)..];
        // Trim trailing slash / query / fragment.
        var cut = rest.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) rest = rest[..cut];
        return rest.Length == 0 ? null : rest;
    }

    private static string AbsoluteSourceUrl(string href)
    {
        if (string.IsNullOrEmpty(href)) return "https://namemc.com/";
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        if (href.StartsWith('/')) return "https://namemc.com" + href;
        return "https://namemc.com/" + href;
    }

    private static HtmlNode? ClosestCard(HtmlNode node)
    {
        var current = node.ParentNode;
        for (var i = 0; current is not null && i < 4; i++)
        {
            var cls = current.GetAttributeValue("class", string.Empty);
            if (!string.IsNullOrEmpty(cls) &&
                (cls.Contains("card", StringComparison.OrdinalIgnoreCase) ||
                 cls.Contains("skin", StringComparison.OrdinalIgnoreCase)))
            {
                return current;
            }
            current = current.ParentNode;
        }
        return node.ParentNode;
    }

    private static SkinVariant DetectVariant(HtmlNode anchor, HtmlNode? card)
    {
        // 1) Some cards mark the model on the anchor (data-model="slim").
        var dataModel = anchor.GetAttributeValue("data-model", null);
        if (IsSlimModelName(dataModel)) return SkinVariant.Slim;

        // 2) The body thumbnail <img> often carries either ?model=slim in its src or a
        //    data-model attribute on the <img> itself.
        var img = anchor.SelectSingleNode(".//img[@src]");
        if (img is not null)
        {
            var imgModel = img.GetAttributeValue("data-model", null);
            if (IsSlimModelName(imgModel)) return SkinVariant.Slim;

            var src = img.GetAttributeValue("src", string.Empty) ?? string.Empty;
            if (src.IndexOf("model=slim", StringComparison.OrdinalIgnoreCase) >= 0) return SkinVariant.Slim;
        }

        // 3) Card-level class hints (e.g. ".model-slim", ".slim").
        if (card is not null)
        {
            var cls = card.GetAttributeValue("class", string.Empty) ?? string.Empty;
            if (cls.IndexOf("slim", StringComparison.OrdinalIgnoreCase) >= 0) return SkinVariant.Slim;
        }

        return SkinVariant.Classic;
    }

    private static bool IsSlimModelName(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Equals("slim", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("alex", StringComparison.OrdinalIgnoreCase));

    private static string DetectThumbnail(HtmlNode anchor, string hash, SkinVariant variant)
    {
        // Prefer the page's own <img src=...> when present (it's already a cached body shot).
        var img = anchor.SelectSingleNode(".//img[@src]");
        if (img is not null)
        {
            var src = img.GetAttributeValue("src", null);
            if (!string.IsNullOrEmpty(src))
            {
                if (src.StartsWith("//")) return "https:" + src;
                if (src.StartsWith("/")) return "https://namemc.com" + src;
                return src;
            }
        }

        // Fallback: build the canonical NameMC body thumbnail.
        var model = variant == SkinVariant.Slim ? "slim" : "classic";
        return $"{ThumbCdn}?id={hash}&model={model}";
    }

    private static string? DetectUploader(HtmlNode? card)
    {
        if (card is null) return null;
        var u = card.SelectSingleNode(".//a[contains(@href, '/profile/') or contains(@href, '/user/')]");
        if (u is null) return null;
        var text = HtmlEntity.DeEntitize(u.InnerText ?? string.Empty).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string[] DetectTags(HtmlNode? card)
    {
        if (card is null) return Array.Empty<string>();
        var tags = card.SelectNodes(".//a[contains(@href, '/tag/')] | .//span[contains(@class, 'tag')]");
        if (tags is null || tags.Count == 0) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var t in tags)
        {
            var text = HtmlEntity.DeEntitize(t.InnerText ?? string.Empty).Trim().Trim('#');
            if (string.IsNullOrEmpty(text)) continue;
            if (seen.Add(text)) list.Add(text);
        }
        return list.ToArray();
    }

    private static int DetectLikes(HtmlNode? card)
    {
        if (card is null) return 0;
        // NameMC's like counters typically live in spans with classes like "card-text" or
        // "text-muted" next to a heart icon. Pull any nested element with data-likes / data-count
        // first, then fall back to scraping numbers next to a "heart" / "like" class anchor.
        var marked = card.SelectSingleNode(".//*[@data-count or @data-likes or @data-heart]");
        if (marked is not null)
        {
            var raw = marked.GetAttributeValue("data-count", null)
                     ?? marked.GetAttributeValue("data-likes", null)
                     ?? marked.GetAttributeValue("data-heart", null);
            if (TryParseCount(raw, out var n)) return n;
        }

        var heart = card.SelectSingleNode(".//*[contains(@class, 'heart') or contains(@class, 'like')]");
        if (heart is not null && TryParseCount(heart.InnerText, out var n2)) return n2;
        return 0;
    }

    private static bool TryParseCount(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        // Strip thousands separators / non-digit decoration.
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
            if (char.IsDigit(ch)) sb.Append(ch);
        if (sb.Length == 0) return false;
        return int.TryParse(sb.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
