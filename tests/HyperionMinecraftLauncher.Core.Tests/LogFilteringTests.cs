using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LogFiltering.Filter"/>. The Logs page on the App side wires
/// its <c>FilteredLogText</c> property through this helper, so the contract pinned here
/// (empty filter passthrough, case-insensitive substring, line-order preservation) directly
/// describes the user-visible filtering behaviour.
/// </summary>
public class LogFilteringTests
{
    [Fact]
    public void EmptyFilter_ReturnsInputUnchanged()
    {
        var input = "alpha" + Environment.NewLine + "beta" + Environment.NewLine + "gamma";

        Assert.Equal(input, LogFiltering.Filter(input, string.Empty));
        Assert.Equal(input, LogFiltering.Filter(input, null!));
    }

    [Fact]
    public void Substring_IsCaseInsensitive()
    {
        var input = string.Join(Environment.NewLine,
            "2026-05-15 12:00 [INFO] launching Minecraft",
            "2026-05-15 12:01 [WARN] disk slow",
            "2026-05-15 12:02 [error] BOOM");

        var filtered = LogFiltering.Filter(input, "error");

        Assert.Contains("[error] BOOM", filtered);
        Assert.DoesNotContain("[INFO]", filtered);
        Assert.DoesNotContain("[WARN]", filtered);
    }

    [Fact]
    public void Substring_MatchesAcrossDifferentCases()
    {
        var input = string.Join(Environment.NewLine,
            "Starting Forge installer",
            "FORGE done",
            "Vanilla launch");

        var filtered = LogFiltering.Filter(input, "forge");

        var lines = filtered.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Starting Forge installer", lines[0]);
        Assert.Equal("FORGE done", lines[1]);
    }

    [Fact]
    public void PreservesLineOrder()
    {
        var input = string.Join(Environment.NewLine,
            "1 hyperion ready",
            "2 unrelated chatter",
            "3 hyperion launching",
            "4 unrelated chatter",
            "5 hyperion exited");

        var filtered = LogFiltering.Filter(input, "hyperion");
        var lines = filtered.Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Equal("1 hyperion ready", lines[0]);
        Assert.Equal("3 hyperion launching", lines[1]);
        Assert.Equal("5 hyperion exited", lines[2]);
    }

    [Fact]
    public void NoMatches_ReturnsEmptyString()
    {
        var input = "alpha" + Environment.NewLine + "beta";

        var filtered = LogFiltering.Filter(input, "zzz-not-there");

        Assert.Equal(string.Empty, filtered);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogFiltering.Filter(string.Empty, "anything"));
        Assert.Equal(string.Empty, LogFiltering.Filter(null!, "anything"));
    }

    [Fact]
    public void HandlesMixedLineEndings()
    {
        // Logs written on Windows may carry \r\n, logs piped from a *nix tool may carry bare \n.
        // The helper must treat both as line separators.
        var input = "alpha\r\nbeta\ngamma alpha\r\ndelta";

        var filtered = LogFiltering.Filter(input, "alpha");
        var lines = filtered.Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("alpha", lines[0]);
        Assert.Equal("gamma alpha", lines[1]);
    }
}
