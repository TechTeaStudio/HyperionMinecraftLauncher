using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Imports a Modrinth <c>.mrpack</c> archive into a fresh Hyperion <see cref="Instance"/>.
///
/// The <c>.mrpack</c> format is a zip with:
/// <list type="bullet">
///   <item>A top-level <c>modrinth.index.json</c> manifest (versionId, name, files[], dependencies).</item>
///   <item>An optional <c>overrides/</c> tree whose contents are copied verbatim into the instance dir.</item>
///   <item>An optional <c>client-overrides/</c> / <c>server-overrides/</c> tree (client wins for the launcher).</item>
/// </list>
///
/// Construction takes the per-instance root directory (e.g.
/// <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/instances/</c>) plus an <see cref="HttpClient"/> used
/// to fetch each <c>files[].downloads[0]</c> URL. The importer never mutates a pre-existing
/// instance folder - it creates a fresh per-id subdirectory and writes everything in there.
/// </summary>
public sealed class ModrinthModpackImporter : IModpackImporter
{
    private const string ManifestEntry = ModpackFormatDetector.ModrinthManifestEntry;
    private const string OverridesPrefix = "overrides/";
    private const string ClientOverridesPrefix = "client-overrides/";

    private readonly string _instancesRoot;
    private readonly HttpClient _http;

    /// <summary>
    /// Construct against a per-instance root directory + an HTTP client. The constructor
    /// only validates its arguments; the directory is created lazily on first import.
    /// </summary>
    public ModrinthModpackImporter(string instancesRoot, HttpClient http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instancesRoot);
        _instancesRoot = instancesRoot;
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<Instance> ImportAsync(
        string archivePath,
        string? targetInstanceName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Modpack archive not found.", archivePath);

        ModpackManifest manifest;
        await using (var fs = File.OpenRead(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            manifest = ParseManifest(zip);
        }

        var instanceId = Guid.NewGuid().ToString("N");
        var instanceDir = Path.Combine(_instancesRoot, instanceId);
        Directory.CreateDirectory(instanceDir);

        var name = string.IsNullOrWhiteSpace(targetInstanceName) ? manifest.Name : targetInstanceName;

        // 1) Download manifest-declared files. Reopen the zip per-file would be wasteful; we
        //    only stream the network here, so the zip stays closed for the duration.
        var total = Math.Max(1, manifest.Files.Count);
        var completed = 0;
        foreach (var file in manifest.Files)
        {
            if (!file.Required) { completed++; continue; }
            cancellationToken.ThrowIfCancellationRequested();

            var dest = Path.Combine(instanceDir, file.TargetPath.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            await DownloadOneAsync(file.DownloadUrl, dest, cancellationToken).ConfigureAwait(false);
            completed++;
            progress?.Report((double)completed / total);
        }

        // 2) Copy overrides tree on top.
        if (!string.IsNullOrEmpty(manifest.OverridesPath))
        {
            await using var fs = File.OpenRead(archivePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            CopyOverrides(zip, manifest.OverridesPath!, instanceDir, cancellationToken);
        }

        // Final progress tick: importing is done.
        progress?.Report(1.0);

        return new Instance
        {
            Id = instanceId,
            Name = name,
            VersionId = manifest.MinecraftVersion,
            Loader = manifest.Loader,
            LoaderVersion = manifest.LoaderVersion,
            GameDirectory = instanceDir,
            IconKey = InstanceIcons.Chest,
        };
    }

    /// <summary>
    /// Parse <c>modrinth.index.json</c> out of an open zip archive. Public so tests can
    /// feed a synthetic zip without the importer touching the disk.
    /// </summary>
    public static ModpackManifest ParseManifest(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var entry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException($"Missing '{ManifestEntry}' inside the .mrpack archive.");

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = ReadString(root, "name") ?? "Modpack";
        var version = ReadString(root, "versionId") ?? string.Empty;
        var (mcVersion, loader, loaderVersion) = ReadDependencies(root);
        var files = ReadFiles(root);

        // Detect overrides folder by looking inside the zip - prefer overrides/, fall back to
        // client-overrides/. Server-overrides are ignored on the launcher side.
        string? overridesPath = null;
        foreach (var e in zip.Entries)
        {
            if (e.FullName.StartsWith(OverridesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                overridesPath = "overrides";
                break;
            }
        }
        if (overridesPath is null)
        {
            foreach (var e in zip.Entries)
            {
                if (e.FullName.StartsWith(ClientOverridesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    overridesPath = "client-overrides";
                    break;
                }
            }
        }

        return new ModpackManifest
        {
            Name = name,
            Version = version,
            MinecraftVersion = mcVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            Files = files,
            OverridesPath = overridesPath,
        };
    }

    private static (string MinecraftVersion, ModLoader Loader, string? LoaderVersion) ReadDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
            return (string.Empty, ModLoader.None, null);

        var mcVersion = ReadString(deps, "minecraft") ?? string.Empty;

        // Modrinth's dependencies map encodes the loader as a kind-specific key. Only one
        // loader-key should be present per pack; we probe in priority order.
        if (deps.TryGetProperty("fabric-loader", out var f) && f.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Fabric, f.GetString());
        if (deps.TryGetProperty("forge", out var forge) && forge.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Forge, forge.GetString());
        if (deps.TryGetProperty("neoforge", out var neo) && neo.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.NeoForge, neo.GetString());
        if (deps.TryGetProperty("quilt-loader", out var quilt) && quilt.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Quilt, quilt.GetString());

        return (mcVersion, ModLoader.None, null);
    }

    private static IReadOnlyList<ModpackFile> ReadFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModpackFile>();

        var list = new List<ModpackFile>(files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
        {
            var path = ReadString(file, "path");
            if (string.IsNullOrWhiteSpace(path)) continue;

            string? url = null;
            if (file.TryGetProperty("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in downloads.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String)
                    {
                        url = d.GetString();
                        if (!string.IsNullOrWhiteSpace(url)) break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(url)) continue;

            string? sha1 = null;
            if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
                sha1 = ReadString(hashes, "sha1");

            long? size = null;
            if (file.TryGetProperty("fileSize", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
                if (sizeEl.TryGetInt64(out var s)) size = s;

            // env.client = "required" | "optional" | "unsupported". Default to required when missing.
            var required = true;
            if (file.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                var client = ReadString(env, "client");
                if (string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase))
                    continue; // skip server-only files entirely
                if (string.Equals(client, "optional", StringComparison.OrdinalIgnoreCase))
                    required = false;
            }

            list.Add(new ModpackFile
            {
                TargetPath = path!,
                DownloadUrl = url!,
                Sha1 = sha1,
                FileSizeBytes = size,
                Required = required,
            });
        }
        return list;
    }

    private static void CopyOverrides(ZipArchive zip, string overridesRoot, string instanceDir, CancellationToken cancellationToken)
    {
        var prefix = overridesRoot.EndsWith('/') ? overridesRoot : overridesRoot + "/";
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Directory entries (trailing slash) carry no bytes; just ensure the folder exists.
            var relative = entry.FullName.Substring(prefix.Length);
            if (string.IsNullOrEmpty(relative)) continue;
            var dest = Path.Combine(instanceDir, relative.Replace('/', Path.DirectorySeparatorChar));

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            using var src = entry.Open();
            using var fs = File.Create(dest);
            src.CopyTo(fs);
        }
    }

    private async Task DownloadOneAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fs = File.Create(destPath);
        await src.CopyToAsync(fs, 81920, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
