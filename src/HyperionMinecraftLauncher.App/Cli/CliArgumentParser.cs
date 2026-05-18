using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Cli;

/// <summary>
/// Pure (no side effects) argv parser. Returns the matching <see cref="CliCommand"/> variant.
/// Anything containing a recognized command flag is greedy: the first recognized command wins,
/// so the order of arguments doesn't matter for the top-level command but does for its options.
/// </summary>
/// <remarks>
/// Recognized top-level commands (mutually exclusive):
/// <list type="bullet">
///   <item><c>--help</c> / <c>-h</c> / <c>-?</c></item>
///   <item><c>--version</c> / <c>-v</c></item>
///   <item><c>--list-instances</c></item>
///   <item><c>--list-versions [--type release|snapshot|all]</c></item>
///   <item><c>--launch &lt;id&gt; [--username NAME] [--server HOST]</c></item>
/// </list>
/// An empty argv (or one without any <c>--</c> arg) yields <see cref="CliCommand.None"/>;
/// <see cref="CliEntryPoint"/> treats that as "fall through to the GUI."
/// </remarks>
public static class CliArgumentParser
{
    /// <summary>Parse the supplied argv into a <see cref="CliCommand"/>.</summary>
    public static CliCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0) return new CliCommand.None();

        // Scan for the first recognized command flag. Unknown flags upstream of it are an error.
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    return new CliCommand.Help();

                case "--version":
                case "-v":
                    return new CliCommand.Version();

                case "--list-instances":
                    return new CliCommand.ListInstances();

                case "--list-versions":
                    return ParseListVersions(args, i + 1);

                case "--launch":
                    return ParseLaunch(args, i + 1);

                default:
                    // Unknown flag - keep scanning, BUT: if it looks like a flag (starts with "-"),
                    // surface it as Invalid so the user gets a useful error instead of None.
                    if (a.StartsWith("--", StringComparison.Ordinal) || a.StartsWith("-", StringComparison.Ordinal))
                        return new CliCommand.Invalid($"Unknown option: {a}");
                    break;
            }
        }

        // No "--*" command flag found at all -> GUI mode.
        return new CliCommand.None();
    }

    private static CliCommand ParseListVersions(string[] args, int start)
    {
        string? type = null;
        for (var i = start; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type":
                    if (i + 1 >= args.Length)
                        return new CliCommand.Invalid("--type requires a value (release|snapshot|all).");
                    type = args[i + 1];
                    if (type is not ("release" or "snapshot" or "all"))
                        return new CliCommand.Invalid($"Invalid --type value '{type}'. Use release, snapshot or all.");
                    i++;
                    break;
                default:
                    return new CliCommand.Invalid($"Unknown option for --list-versions: {args[i]}");
            }
        }
        return new CliCommand.ListVersions(type);
    }

    private static CliCommand ParseLaunch(string[] args, int start)
    {
        if (start >= args.Length || args[start].StartsWith("--", StringComparison.Ordinal))
            return new CliCommand.Invalid("--launch requires an instance id.");

        var instanceId = args[start];
        string? username = null;
        string? server = null;

        for (var i = start + 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--username":
                    if (i + 1 >= args.Length)
                        return new CliCommand.Invalid("--username requires a value.");
                    username = args[i + 1];
                    i++;
                    break;
                case "--server":
                    if (i + 1 >= args.Length)
                        return new CliCommand.Invalid("--server requires a value.");
                    server = args[i + 1];
                    i++;
                    break;
                default:
                    return new CliCommand.Invalid($"Unknown option for --launch: {args[i]}");
            }
        }

        return new CliCommand.Launch(instanceId, username, server);
    }
}
