using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;

/// <summary>
/// <see cref="IModRepository"/> talking to the CurseForge v1 REST API
/// (<c>https://api.curseforge.com/v1</c>). Unlike Modrinth, CurseForge requires every
/// developer to obtain their own API key from <c>console.curseforge.com</c> - we never
/// ship one. When the configured key is empty the launcher gracefully disables the
/// integration: <see cref="SearchAsync"/> returns an empty list (and warns once),
/// while <see cref="ListFilesAsync"/> / <see cref="DownloadAsync"/> throw
/// <see cref="NotSupportedException"/> so the UI can show a "configure your key" hint.
/// </summary>
public sealed class CurseForgeRepository : IModRepository
{
    /// <summary>CurseForge's game id for Minecraft, used as the <c>gameId</c> query param.</summary>
    public const int MinecraftGameId = 432;

    /// <summary>Base URL for the CurseForge v1 REST API.</summary>
    public const string DefaultBaseAddress = "https://api.curseforge.com/v1/";

    private const string KeyMissingMessage = "CurseForge API key not configured";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILauncherLogger _logger;
    private bool _keyWarningEmitted;

    /// <summary>
    /// Construct the repository with an injected <see cref="HttpClient"/>. The base address
    /// is set to <see cref="DefaultBaseAddress"/> when missing. An empty <paramref name="apiKey"/>
    /// disables the network integration; the constructor itself never throws.
    /// </summary>
    public CurseForgeRepository(HttpClient http, string apiKey, ILauncherLogger logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey ?? string.Empty;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(DefaultBaseAddress);
    }

    /// <inheritdoc />
    public ModSource Source => ModSource.CurseForge;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrEmpty(_apiKey))
        {
            if (!_keyWarningEmitted)
            {
                _logger.Warn(KeyMissingMessage + " - paste a key into Settings to enable CurseForge search.");
                _keyWarningEmitted = true;
            }
            return Array.Empty<Mod>();
        }

        var sb = new StringBuilder("mods/search?gameId=").Append(MinecraftGameId);
        if (!string.IsNullOrEmpty(query.Query))
            sb.Append("&searchFilter=").Append(Uri.EscapeDataString(query.Query));
        if (!string.IsNullOrEmpty(query.GameVersion))
            sb.Append("&gameVersion=").Append(Uri.EscapeDataString(query.GameVersion));
        if (query.Loader is { } loader && TryLoaderType(loader, out var loaderType))
            sb.Append("&modLoaderType=").Append(loaderType);
        sb.Append("&pageSize=").Append(query.Limit);
        sb.Append("&index=").Append(query.Offset);

        using var request = NewRequest(HttpMethod.Get, sb.ToString());
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseSearchHits(stream);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModFile>> ListFilesAsync(string modId, string? gameVersion, ModLoader? loader, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        if (string.IsNullOrEmpty(_apiKey))
            throw new NotSupportedException(KeyMissingMessage);

        var sb = new StringBuilder("mods/").Append(Uri.EscapeDataString(modId)).Append("/files?");
        var first = true;
        if (!string.IsNullOrEmpty(gameVersion))
        {
            sb.Append("gameVersion=").Append(Uri.EscapeDataString(gameVersion));
            first = false;
        }
        if (loader is { } l && TryLoaderType(l, out var loaderType))
        {
            if (!first) sb.Append('&');
            sb.Append("modLoaderType=").Append(loaderType);
        }

        using var request = NewRequest(HttpMethod.Get, sb.ToString());
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseFiles(stream, modId);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(ModFile file, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrEmpty(_apiKey))
            throw new NotSupportedException(KeyMissingMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? file.FileSize;
        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[81920];
        long readSoFar = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readSoFar += read;
            if (progress is not null && total > 0)
                progress.Report((double)readSoFar / total);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        req.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        return req;
    }

    /// <summary>CurseForge's numeric loader-type identifiers.</summary>
    /// <remarks>1=Forge, 4=Fabric, 5=Quilt, 6=NeoForge per their API docs.</remarks>
    private static bool TryLoaderType(ModLoader loader, out int code)
    {
        switch (loader)
        {
            case ModLoader.Forge: code = 1; return true;
            case ModLoader.Fabric: code = 4; return true;
            case ModLoader.Quilt: code = 5; return true;
            case ModLoader.NeoForge: code = 6; return true;
            case ModLoader.LegacyForge: code = 1; return true;
            default: code = 0; return false;
        }
    }

    /// <summary>Parse a CurseForge <c>/mods/search</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<Mod> ParseSearchHits(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<Mod>();

        var list = new List<Mod>(data.GetArrayLength());
        foreach (var el in data.EnumerateArray())
        {
            var id = ReadInt64(el, "id").ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (id == "0") continue;

            var iconUri = el.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object
                ? ReadString(logo, "url")
                : null;
            var pageUri = el.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object
                ? ReadString(links, "websiteUrl")
                : null;
            var author = string.Empty;
            if (el.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
                foreach (var a in authors.EnumerateArray())
                {
                    var name = ReadString(a, "name");
                    if (!string.IsNullOrEmpty(name)) { author = name; break; }
                }

            var categories = new List<string>();
            if (el.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                foreach (var c in cats.EnumerateArray())
                {
                    var name = ReadString(c, "name");
                    if (!string.IsNullOrEmpty(name)) categories.Add(name);
                }

            list.Add(new Mod
            {
                Id = id,
                Slug = ReadString(el, "slug") ?? id,
                Name = ReadString(el, "name") ?? id,
                Description = ReadString(el, "summary") ?? string.Empty,
                AuthorDisplay = author,
                IconUri = iconUri,
                PageUri = pageUri,
                Categories = categories,
                Downloads = ReadInt64(el, "downloadCount"),
                Source = ModSource.CurseForge,
            });
        }
        return list;
    }

    /// <summary>Parse a CurseForge <c>/mods/{id}/files</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<ModFile> ParseFiles(Stream stream, string modId)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModFile>();

        var list = new List<ModFile>(data.GetArrayLength());
        foreach (var f in data.EnumerateArray())
        {
            var fileId = ReadInt64(f, "id").ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (fileId == "0") continue;
            var url = ReadString(f, "downloadUrl") ?? string.Empty;
            if (string.IsNullOrEmpty(url)) continue;

            var gameVersions = new List<string>();
            if (f.TryGetProperty("gameVersions", out var gvs) && gvs.ValueKind == JsonValueKind.Array)
                foreach (var v in gvs.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.String)
                        gameVersions.Add(v.GetString() ?? string.Empty);

            string? sha1 = null;
            if (f.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in hashes.EnumerateArray())
                {
                    // CurseForge encodes hash algorithm as a numeric code: 1=Sha1, 2=Md5.
                    var algo = ReadInt64(h, "algo");
                    if (algo == 1)
                    {
                        sha1 = ReadString(h, "value");
                        break;
                    }
                }
            }

            list.Add(new ModFile
            {
                ModId = modId,
                FileId = fileId,
                DisplayName = ReadString(f, "displayName") ?? fileId,
                Filename = ReadString(f, "fileName"),
                DownloadUrl = url,
                GameVersions = gameVersions,
                Loaders = Array.Empty<ModLoader>(),
                FileSize = ReadInt64(f, "fileLength"),
                Sha1 = sha1,
            });
        }
        return list;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static long ReadInt64(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : (long)v.GetDouble(),
            _ => 0,
        };
    }
}
