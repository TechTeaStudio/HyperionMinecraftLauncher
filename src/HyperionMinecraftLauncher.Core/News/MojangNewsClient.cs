using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.News;

/// <summary>
/// Fetches the Mojang launcher's own news feed (<c>https://launchercontent.mojang.com/news.json</c>).
/// This is the same endpoint the official Minecraft launcher polls to populate its Play / News pages,
/// so what our users see matches what they'd see in Mojang's launcher.
/// </summary>
public sealed class MojangNewsClient : INewsClient
{
    private const string FeedUrl = "https://launchercontent.mojang.com/news.json";
    private const string ImageBaseUrl = "https://launchercontent.mojang.com";

    private readonly HttpClient _http;

    /// <summary>Default constructor: builds its own <see cref="HttpClient"/>.</summary>
    public MojangNewsClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }) { }

    /// <summary>Test / advanced constructor accepting a pre-configured client (or one with a fake handler).</summary>
    public MojangNewsClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync(FeedUrl, cancellationToken).ConfigureAwait(false);
            return Parse(stream);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Network outage, DNS failure, schema drift - all yield an empty list rather than blowing up.
            // Logging is intentionally left to the service layer that wraps this client.
            return Array.Empty<NewsEntry>();
        }
    }

    /// <summary>Parse a JSON stream in the launcher news schema. Public for direct unit tests.</summary>
    public static IReadOnlyList<NewsEntry> Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return Array.Empty<NewsEntry>();

        var list = new List<NewsEntry>(entries.GetArrayLength());
        foreach (var el in entries.EnumerateArray())
        {
            var title = ReadString(el, "title") ?? string.Empty;
            var category = ReadString(el, "category") ?? string.Empty;
            var date = ReadString(el, "date") ?? string.Empty;
            var text = ReadString(el, "text") ?? string.Empty;
            var image = ReadImageUrl(el, "newsPageImage") ?? ReadImageUrl(el, "playPageImage");
            list.Add(new NewsEntry
            {
                Title = title,
                Category = category,
                Date = date,
                Text = text,
                ImageUrl = image,
                ReadMoreLink = ReadString(el, "readMoreLink"),
                Id = ReadString(el, "id"),
            });
        }
        return list;
    }

    private static string? ReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? ReadImageUrl(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var img) || img.ValueKind != JsonValueKind.Object)
            return null;
        var url = ReadString(img, "url");
        if (string.IsNullOrEmpty(url))
            return null;
        // The feed gives relative paths like "/images/...". Resolve against the CDN.
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : ImageBaseUrl + url;
    }
}
