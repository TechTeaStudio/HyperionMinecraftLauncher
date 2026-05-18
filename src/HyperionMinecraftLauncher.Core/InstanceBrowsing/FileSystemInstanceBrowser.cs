using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// File-system-backed <see cref="IInstanceBrowser"/>. Resolves the per-instance root to
/// <see cref="Instance.GameDirectory"/> when set, otherwise the platform default <c>.minecraft</c>.
/// Each list method runs the I/O on the thread pool so callers can hand us a CancellationToken
/// without blocking the UI thread on the disk scan.
/// </summary>
public sealed class FileSystemInstanceBrowser : IInstanceBrowser
{
    private readonly IServersStore _serversStore;

    /// <summary>Default ctor - wraps a plain <see cref="FileServersStore"/> for <c>servers.dat</c> reads.</summary>
    public FileSystemInstanceBrowser() : this(new FileServersStore())
    {
    }

    /// <summary>Test-friendly ctor: inject a fake servers store.</summary>
    public FileSystemInstanceBrowser(IServersStore serversStore)
    {
        _serversStore = serversStore ?? throw new ArgumentNullException(nameof(serversStore));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScreenshotEntry>> ListScreenshotsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var dir = Path.Combine(root, "screenshots");

        return Task.Run<IReadOnlyList<ScreenshotEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) return Array.Empty<ScreenshotEntry>();

            var entries = new List<ScreenshotEntry>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    PngIhdrReader.TryReadSize(path, out var w, out var h);
                    entries.Add(new ScreenshotEntry
                    {
                        FullPath = info.FullName,
                        Filename = info.Name,
                        TakenAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        SizeBytes = info.Length,
                        Width = w,
                        Height = h,
                    });
                }
                catch
                {
                    // Skip unreadable files - never crash the whole list because one PNG is bad.
                }
            }

            return entries
                .OrderByDescending(e => e.TakenAt)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorldEntry>> ListWorldsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var savesDir = Path.Combine(root, "saves");

        return Task.Run<IReadOnlyList<WorldEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(savesDir)) return Array.Empty<WorldEntry>();

            var entries = new List<WorldEntry>();
            foreach (var dir in Directory.EnumerateDirectories(savesDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var levelDatPath = Path.Combine(dir, "level.dat");
                if (!File.Exists(levelDatPath)) continue;

                var folderName = Path.GetFileName(dir);
                var info = LevelDatReader.Read(levelDatPath);
                long size = TryComputeDirectorySize(dir);

                entries.Add(new WorldEntry
                {
                    FullPath = dir,
                    FolderName = folderName,
                    DisplayName = info?.LevelName ?? folderName,
                    LastPlayedIso = info?.LastPlayedIso,
                    SizeBytes = size,
                    GameMode = info?.GameType,
                    Version = info?.VersionName,
                });
            }

            return entries
                .OrderByDescending(e => e.LastPlayedIso ?? string.Empty)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ServerListEntry>> ListServersAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var path = Path.Combine(root, "servers.dat");
        return _serversStore.LoadAsync(path, cancellationToken);
    }

    private static string ResolveRoot(Instance instance)
    {
        return !string.IsNullOrWhiteSpace(instance.GameDirectory)
            ? instance.GameDirectory
            : DefaultMinecraftInstallationLocator.ResolveRoot();
    }

    private static long TryComputeDirectorySize(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* skip unreadable file */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
