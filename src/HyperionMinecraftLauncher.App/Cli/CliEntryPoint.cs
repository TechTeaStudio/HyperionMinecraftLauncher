using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Cli;

/// <summary>
/// Dispatch table for the Hyperion CLI mode (v0.28 T11.5). <see cref="RunAsync"/> takes the
/// already-parsed argv plus the minimum DI a headless run needs (<see cref="IMinecraftLauncherService"/>
/// and <see cref="ILauncherLogger"/>) and writes to <c>stdout</c>. Returns a process exit code
/// the caller hands to <c>Environment.Exit</c>.
/// </summary>
public static class CliEntryPoint
{
    /// <summary>The version string printed by <c>--version</c>. Synced with the .csproj
    /// <c>&lt;Version&gt;</c> field by hand on each release - one source of truth lives in
    /// the project file but the CLI mustn't take a runtime Assembly metadata dependency.</summary>
    public const string CliVersion = "0.28.0";

    /// <summary>Dispatch the supplied argv. Side effects: stdout writes, file logger entries.</summary>
    public static async Task<int> RunAsync(string[] args, IMinecraftLauncherService service, ILauncherLogger logger)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);

        var command = CliArgumentParser.Parse(args);
        var stdout = Console.Out;
        var stderr = Console.Error;

        return command switch
        {
            CliCommand.None => 0,
            CliCommand.Help => WriteHelp(stdout),
            CliCommand.Version => WriteVersion(stdout),
            CliCommand.ListInstances => await ListInstancesAsync(service, stdout, stderr, logger).ConfigureAwait(false),
            CliCommand.ListVersions lv => await ListVersionsAsync(service, lv.Type, stdout, stderr, logger).ConfigureAwait(false),
            CliCommand.Launch l => await LaunchAsync(service, l, stdout, stderr, logger).ConfigureAwait(false),
            CliCommand.Invalid inv => WriteInvalid(stderr, inv.Reason),
            _ => 0,
        };
    }

    private static int WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("Hyperion Minecraft Launcher - CLI mode");
        stdout.WriteLine();
        stdout.WriteLine("Usage:");
        stdout.WriteLine("  HyperionMinecraftLauncher [command] [options]");
        stdout.WriteLine();
        stdout.WriteLine("Commands:");
        stdout.WriteLine("  --help, -h                       Show this help text.");
        stdout.WriteLine("  --version, -v                    Print the launcher version and exit.");
        stdout.WriteLine("  --list-instances                 List every saved Hyperion instance.");
        stdout.WriteLine("  --list-versions [--type T]       List Minecraft versions from the Mojang manifest.");
        stdout.WriteLine("                                   T = release | snapshot | all (default: release).");
        stdout.WriteLine("  --launch <id> [options]          Install (if needed) and launch the given instance.");
        stdout.WriteLine("    --username NAME                Override the offline username.");
        stdout.WriteLine("    --server HOST[:PORT]           Quick Play: connect to the given server on launch.");
        stdout.WriteLine();
        stdout.WriteLine("Running with no '--' flag launches the regular GUI.");
        return 0;
    }

    private static int WriteVersion(TextWriter stdout)
    {
        stdout.WriteLine(CliVersion);
        return 0;
    }

    private static int WriteInvalid(TextWriter stderr, string reason)
    {
        stderr.WriteLine($"Error: {reason}");
        stderr.WriteLine("Use --help for usage information.");
        return 2;
    }

    private static async Task<int> ListInstancesAsync(
        IMinecraftLauncherService service, TextWriter stdout, TextWriter stderr, ILauncherLogger logger)
    {
        try
        {
            var instances = await service.ListInstancesAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var i in instances)
            {
                // Tab-separated: id, name, version, loader. Stable shape so scripts can parse it.
                stdout.WriteLine($"{i.Id}\t{i.Name}\t{i.VersionId}\t{i.Loader}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error("CLI --list-instances failed.", ex);
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ListVersionsAsync(
        IMinecraftLauncherService service, string? typeFilter,
        TextWriter stdout, TextWriter stderr, ILauncherLogger logger)
    {
        try
        {
            var versions = await service.ListVersionsAsync(CancellationToken.None).ConfigureAwait(false);

            // null / "release" / "snapshot" / "all". null defaults to "release" since that's
            // the most common script use ("give me the list of stable versions").
            var effective = string.IsNullOrEmpty(typeFilter) ? "release" : typeFilter;
            var filtered = effective == "all"
                ? versions
                : versions.Where(v => string.Equals(v.Type, effective, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var v in filtered)
            {
                stdout.WriteLine($"{v.Name}\t{v.Type}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error("CLI --list-versions failed.", ex);
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> LaunchAsync(
        IMinecraftLauncherService service, CliCommand.Launch launch,
        TextWriter stdout, TextWriter stderr, ILauncherLogger logger)
    {
        try
        {
            var instances = await service.ListInstancesAsync(CancellationToken.None).ConfigureAwait(false);
            var instance = instances.FirstOrDefault(i =>
                string.Equals(i.Id, launch.InstanceId, StringComparison.OrdinalIgnoreCase));

            if (instance is null)
            {
                stderr.WriteLine($"Error: no instance with id '{launch.InstanceId}'.");
                return 3;
            }

            // Offline session is always available; Microsoft requires interactive device-code which
            // is hostile in a CLI flow. The user passes --username to set the offline name.
            var username = string.IsNullOrWhiteSpace(launch.Username) ? "Steve" : launch.Username!;
            var session = await service.AuthenticateAsync(
                new AuthRequest { Mode = AuthMode.Offline, Username = username },
                CancellationToken.None).ConfigureAwait(false);

            // --server HOST[:PORT] -> Quick Play multiplayer deep-link.
            var quickPlay = ParseServerArg(launch.Server);

            var request = new LaunchRequest
            {
                VersionName = instance.VersionId,
                Session = session,
                GameDirectory = instance.GameDirectory,
                MinimumRamMb = instance.MinimumRamMb,
                MaximumRamMb = instance.MaximumRamMb,
                QuickPlay = quickPlay,
            };

            // Stream progress to stdout so the calling script can watch the install pipeline.
            var progress = new Progress<LaunchProgress>(p =>
            {
                var pct = p.Fraction.HasValue ? $"{p.Fraction.Value * 100:F1}%" : "-";
                stdout.WriteLine($"[{p.Stage}] {pct} {p.CurrentItem}");
            });

            var result = await service.LaunchAsync(request, progress, CancellationToken.None).ConfigureAwait(false);
            stdout.WriteLine($"Launched {result.VersionName} (pid={result.ProcessId}).");
            return result.ProcessId;
        }
        catch (Exception ex)
        {
            logger.Error("CLI --launch failed.", ex);
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Parse <c>--server HOST[:PORT]</c> into a <see cref="QuickPlay.Multiplayer"/> or
    /// <see cref="QuickPlay.None"/>. An unparseable port falls back to the Minecraft default 25565.
    /// Exposed for tests; production code reaches it through <see cref="LaunchAsync"/>.
    /// </summary>
    public static QuickPlay ParseServerArg(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new QuickPlay.None();

        var idx = raw.LastIndexOf(':');
        if (idx > 0 && idx < raw.Length - 1)
        {
            var host = raw[..idx];
            if (int.TryParse(raw[(idx + 1)..], out var port) && port is > 0 and < 65536)
                return new QuickPlay.Multiplayer(host, port);
        }
        return new QuickPlay.Multiplayer(raw, 25565);
    }
}
