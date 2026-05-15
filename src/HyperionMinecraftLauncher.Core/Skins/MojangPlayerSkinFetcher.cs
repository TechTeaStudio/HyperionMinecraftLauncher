using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Looks up a player's skin via Mojang's session server:
/// <c>https://sessionserver.mojang.com/session/minecraft/profile/{uuid}?unsigned=false</c>
/// then base64-decodes the <c>textures</c> property to find the skin / cape URLs and
/// downloads the PNGs. Network or schema failures degrade silently to <c>null</c> so
/// the UI keeps the default Steve.
/// </summary>
public sealed class MojangPlayerSkinFetcher : IPlayerSkinFetcher
{
    private const string ProfileUrlTemplate =
        "https://sessionserver.mojang.com/session/minecraft/profile/{0}?unsigned=false";

    private readonly HttpClient _http;

    public MojangPlayerSkinFetcher() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }) { }

    public MojangPlayerSkinFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<PlayerSkinInfo?> FetchAsync(string uuid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return null;

        var trimmed = uuid.Replace("-", string.Empty);
        if (trimmed.Length != 32) return null;

        try
        {
            var url = string.Format(ProfileUrlTemplate, trimmed.ToLowerInvariant());
            using var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var profile = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            // properties: [{ name: "textures", value: <base64> }]
            if (!profile.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Array)
                return null;

            string? texturesB64 = null;
            foreach (var p in props.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var n) && n.GetString() == "textures"
                    && p.TryGetProperty("value", out var v) && v.GetString() is { } s)
                {
                    texturesB64 = s;
                    break;
                }
            }
            if (texturesB64 is null) return null;

            var inner = Encoding.UTF8.GetString(Convert.FromBase64String(texturesB64));
            using var texturesDoc = JsonDocument.Parse(inner);
            if (!texturesDoc.RootElement.TryGetProperty("textures", out var textures)
                || textures.ValueKind != JsonValueKind.Object)
                return null;

            string? skinUrl = null;
            bool slim = false;
            if (textures.TryGetProperty("SKIN", out var skinNode) && skinNode.ValueKind == JsonValueKind.Object)
            {
                if (skinNode.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    skinUrl = u.GetString();
                if (skinNode.TryGetProperty("metadata", out var meta)
                    && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("model", out var model)
                    && model.ValueKind == JsonValueKind.String
                    && string.Equals(model.GetString(), "slim", StringComparison.OrdinalIgnoreCase))
                {
                    slim = true;
                }
            }
            if (string.IsNullOrEmpty(skinUrl)) return null;

            var skinPng = await _http.GetByteArrayAsync(skinUrl, cancellationToken).ConfigureAwait(false);

            byte[]? capePng = null;
            if (textures.TryGetProperty("CAPE", out var capeNode)
                && capeNode.ValueKind == JsonValueKind.Object
                && capeNode.TryGetProperty("url", out var cu)
                && cu.ValueKind == JsonValueKind.String
                && cu.GetString() is { Length: > 0 } capeUrl)
            {
                try
                {
                    capePng = await _http.GetByteArrayAsync(capeUrl, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    capePng = null;
                }
            }

            return new PlayerSkinInfo { SkinPng = skinPng, IsSlim = slim, CapePng = capePng };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
