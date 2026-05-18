namespace TechTeaStudio.HyperionMinecraftLauncher.App.Cli;

/// <summary>
/// Discriminated union of every command the Hyperion CLI dispatcher understands.
/// The parser (<see cref="CliArgumentParser"/>) maps an <c>argv</c> array to one
/// of these variants; <see cref="CliEntryPoint"/> pattern-matches and dispatches.
/// </summary>
/// <remarks>
/// The shape mirrors the spec in T11.5: <c>None</c> for "no CLI args at all" (so
/// the caller falls through to the regular GUI launch), <c>Help</c> for <c>--help</c>,
/// <c>Version</c> for <c>--version</c>, <c>ListInstances</c>, <c>Launch</c> (with id +
/// optional username + optional server) and <c>ListVersions</c> (with optional type filter).
/// Anything else parses to <see cref="Invalid"/> with a human-readable reason.
/// </remarks>
public abstract record CliCommand
{
    /// <summary>No CLI command - argv didn't contain any "--" flags; caller should run the GUI.</summary>
    public sealed record None : CliCommand;

    /// <summary><c>--help</c></summary>
    public sealed record Help : CliCommand;

    /// <summary><c>--version</c></summary>
    public sealed record Version : CliCommand;

    /// <summary><c>--list-instances</c></summary>
    public sealed record ListInstances : CliCommand;

    /// <summary><c>--list-versions [--type release|snapshot|all]</c>. <see cref="Type"/> is null when omitted.</summary>
    public sealed record ListVersions(string? Type) : CliCommand;

    /// <summary><c>--launch &lt;id&gt; [--username NAME] [--server HOST]</c></summary>
    public sealed record Launch(string InstanceId, string? Username, string? Server) : CliCommand;

    /// <summary>A malformed command line. <see cref="Reason"/> is shown to the user.</summary>
    public sealed record Invalid(string Reason) : CliCommand;
}
