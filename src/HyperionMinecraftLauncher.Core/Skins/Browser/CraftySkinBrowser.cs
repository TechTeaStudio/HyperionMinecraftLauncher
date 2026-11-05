using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// <see cref="ISkinBrowser"/> backed by crafty.gg's public JSON API (a NameMC-style player and
/// skin directory). The catalog shows skins that real players are wearing, ranked by how many
/// started wearing them this week, rather than an anonymous upload feed. Skins crafty.gg has
/// banned or deleted, and skins a player hid from their profile, are never shown.
/// </summary>
/// <remarks>
/// <para><c>GET /api/v2/skins/trending?page=N</c> serves 10 skins per page (1-based), so one
/// catalog page is several crafty pages fetched in parallel.</para>
/// <para>The API has no free-text skin search; <see cref="SearchAsync(string, int, int, CancellationToken)"/>
/// looks the query up as a player name through <c>GET /api/v2/players/{name}</c> and lists
/// every skin that player has worn, newest (current) first.</para>
/// </remarks>
public sealed class CraftySkinBrowser : ISkinBrowser
{
    /// <summary>API root.</summary>
    public const string ApiRoot = "https://api.crafty.gg/api/v2";

    /// <summary>Skin page on the website, followed by the skin id.</summary>
    public const string SkinPageStem = "https://crafty.gg/skins/";

    /// <summary>Mojang texture CDN, followed by the texture hash.</summary>
    public const string TextureCdn = "https://textures.minecraft.net/texture/";

    /// <summary>Skins per page of the trending endpoint (fixed by the API).</summary>
    public const int ApiPageSize = 10;

    private const int DefaultLimit = 60;
    private const int MaxLimit = 120;

    private static readonly Regex PlayerName = new("^[A-Za-z0-9_]{1,16}$", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public CraftySkinBrowser(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken)
        => ListTrendingAsync(page: 0, limit, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int page, int limit, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit > 0 ? limit : DefaultLimit, 1, MaxLimit);
        var apiPages = (size + ApiPageSize - 1) / ApiPageSize;
        var first = Math.Max(0, page) * apiPages + 1;

        var requests = Enumerable.Range(first, apiPages)
            .Select(p => GetJsonAsync($"{ApiRoot}/skins/trending?page={p}", cancellationToken));
        var bodies = await Task.WhenAll(requests).ConfigureAwait(false);

        return bodies
            .Where(body => body is not null)
            .SelectMany(body => ParseSkinList(body!))
            .DistinctBy(skin => skin.Id)
            .Take(size)
            .ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        => SearchAsync(query, page: 0, limit, cancellationToken);

    /// <summary>
    /// Every visible skin of the player named <paramref name="query"/>, current skin first.
    /// Queries that can't be a Minecraft name, unknown players and pages after the first
    /// return an empty list.
    /// </summary>
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int page, int limit, CancellationToken cancellationToken)
    {
        var name = (query ?? string.Empty).Trim();
        if (page > 0 || !PlayerName.IsMatch(name)) return Array.Empty<BrowsedSkin>();

        var body = await GetJsonAsync($"{ApiRoot}/players/{name}", cancellationToken).ConfigureAwait(false);
        if (body is null) return Array.Empty<BrowsedSkin>();

        var size = Math.Clamp(limit > 0 ? limit : DefaultLimit, 1, MaxLimit);
        return ParsePlayerSkins(body).Take(size).ToList();
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return await _http.GetByteArrayAsync(skin.PngDownloadUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parse a <c>{ "data": [ skin, ... ] }</c> list (trending), dropping banned / deleted skins.</summary>
    public static IReadOnlyList<BrowsedSkin> ParseSkinList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<BrowsedSkin>();

        var result = new List<BrowsedSkin>();
        foreach (var entry in data.EnumerateArray())
        {
            string? uploader = null;
            if (entry.TryGetProperty("first_player", out var player) && player.ValueKind == JsonValueKind.Object)
                uploader = GetString(player, "username");
            if (TryParseSkin(entry, uploader, isCurrent: false) is { } skin)
                result.Add(skin);
        }
        return result;
    }

    /// <summary>
    /// Parse a <c>{ "data": { "username": ..., "skins": [ ... ] } }</c> player record into its
    /// visible skins, newest first; the first one is marked as the player's current skin.
    /// </summary>
    public static IReadOnlyList<BrowsedSkin> ParsePlayerSkins(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return Array.Empty<BrowsedSkin>();
        if (!data.TryGetProperty("skins", out var skins) || skins.ValueKind != JsonValueKind.Array)
            return Array.Empty<BrowsedSkin>();

        var username = GetString(data, "username");
        var result = new List<BrowsedSkin>();
        foreach (var entry in skins.EnumerateArray())
        {
            if (TryParseSkin(entry, username, isCurrent: result.Count == 0) is { } skin)
                result.Add(skin);
        }
        return result;
    }

    private static BrowsedSkin? TryParseSkin(JsonElement entry, string? uploader, bool isCurrent)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(entry, "banned") || GetBool(entry, "hidden")) return null;
        if (entry.TryGetProperty("deleted_at", out var deleted) && deleted.ValueKind != JsonValueKind.Null) return null;

        var id = GetString(entry, "id");
        var hash = GetString(entry, "hash");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(hash)) return null;

        var png = TextureCdn + hash;
        var likes = entry.TryGetProperty("upvotes_lifetime", out var up) && up.TryGetInt32(out var n) ? n : 0;
        return new BrowsedSkin(
            Id: id,
            SourceUrl: SkinPageStem + id,
            ThumbnailUrl: png,
            PngDownloadUrl: png,
            Variant: GetBool(entry, "slim") ? SkinVariant.Slim : SkinVariant.Classic,
            UploaderName: uploader,
            Tags: Array.Empty<string>(),
            Likes: likes,
            IsLivePlayerSkin: isCurrent);
    }

    // Null for 404 (unknown player); other failures throw so the UI can report them.
    private async Task<string?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
