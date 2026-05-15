using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Production implementation of <see cref="IUnderlyingLauncher"/> that delegates to
/// <see cref="MinecraftLauncher"/>. All translation between CmlLib's types and our
/// neutral DTOs happens here; the public service sits on top of the interface and
/// stays free of CmlLib-specific types.
/// </summary>
public sealed class CmlLibUnderlyingLauncher : IUnderlyingLauncher
{
    private readonly MinecraftLauncher _launcher;

    /// <summary>Construct an underlying launcher with a default <c>.minecraft</c> directory.</summary>
    public CmlLibUnderlyingLauncher()
        : this(new MinecraftLauncher())
    {
    }

    /// <summary>Construct with a specific game directory.</summary>
    public CmlLibUnderlyingLauncher(string gameDirectory)
        : this(new MinecraftLauncher(gameDirectory))
    {
    }

    /// <summary>Construct with a pre-built <see cref="MinecraftLauncher"/> (useful for advanced wiring).</summary>
    public CmlLibUnderlyingLauncher(MinecraftLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionMetadata>> GetAllVersionsAsync(CancellationToken cancellationToken)
    {
        var versions = await _launcher.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<VersionMetadata>(capacity: 256);
        foreach (var v in versions)
        {
            result.Add(new VersionMetadata
            {
                Name = v.Name ?? string.Empty,
                Type = v.Type ?? string.Empty,
                ReleaseTime = v.ReleaseTime,
            });
        }

        // CmlLib's VersionMetadataCollection iterates manifest-order (~newest-first already), but we
        // re-sort defensively so callers can rely on date-descending ordering regardless of which
        // CmlLib version we're pinned to.
        result.Sort(static (a, b) => b.ReleaseTime.CompareTo(a.ReleaseTime));
        return result;
    }

    /// <inheritdoc />
    public async Task InstallAsync(
        string versionName,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionName))
            throw new ArgumentException("versionName is required", nameof(versionName));

        // CmlLib exposes two parallel progress channels (file count + bytes). We project both into
        // the neutral LaunchProgress stream.
        IProgress<InstallerProgressChangedEventArgs>? fileProgress = null;
        IProgress<ByteProgress>? byteProgress = null;

        if (progress is not null)
        {
            fileProgress = new SyncProgress<InstallerProgressChangedEventArgs>(args =>
            {
                var fraction = args.TotalTasks > 0
                    ? (double)args.ProgressedTasks / args.TotalTasks
                    : (double?)null;

                progress.Report(new LaunchProgress
                {
                    Stage = StageFromInstallerEvent(args.EventType),
                    Fraction = fraction,
                    CurrentItem = args.Name,
                });
            });

            byteProgress = new SyncProgress<ByteProgress>(args =>
            {
                // ByteProgress is a readonly struct with TotalBytes / ProgressedBytes fields.
                var fraction = args.TotalBytes > 0
                    ? (double)args.ProgressedBytes / args.TotalBytes
                    : (double?)null;

                progress.Report(new LaunchProgress
                {
                    Stage = "Downloading bytes",
                    Fraction = fraction,
                    CurrentItem = null,
                });
            });
        }

        await _launcher.InstallAsync(versionName, fileProgress, byteProgress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> StartProcessAsync(
        string versionName,
        string username,
        string uuid,
        string accessToken,
        int? minimumRamMb,
        int? maximumRamMb,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionName))
            throw new ArgumentException("versionName is required", nameof(versionName));
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("username is required", nameof(username));

        var session = new MSession
        {
            Username = username,
            UUID = uuid,
            AccessToken = accessToken,
        };

        var options = new MLaunchOption
        {
            Session = session,
        };

        if (minimumRamMb is int min) options.MinimumRamMb = min;
        if (maximumRamMb is int max) options.MaximumRamMb = max;

        var process = await _launcher.BuildProcessAsync(versionName, options, cancellationToken).ConfigureAwait(false);
        process.Start();
        return process.Id;
    }

    private static string StageFromInstallerEvent(InstallerEventType type) => type switch
    {
        InstallerEventType.Queued => "Downloading library",
        InstallerEventType.Done => "Install done",
        _ => "Installing",
    };
}
