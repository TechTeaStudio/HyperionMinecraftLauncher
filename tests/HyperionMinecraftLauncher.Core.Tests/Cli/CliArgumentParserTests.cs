using System;
using TechTeaStudio.HyperionMinecraftLauncher.App.Cli;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Cli;

/// <summary>
/// Table-driven tests against every known argv shape the CLI dispatcher accepts.
/// The parser is pure, so each row is one isolated <see cref="CliArgumentParser.Parse"/> call.
/// </summary>
public class CliArgumentParserTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsNone()
    {
        var result = CliArgumentParser.Parse(Array.Empty<string>());
        Assert.IsType<CliCommand.None>(result);
    }

    [Fact]
    public void Parse_PositionalArgsWithoutFlags_ReturnsNone()
    {
        // Avalonia / Windows shell sometimes forwards a path argv on file-association launches;
        // the launcher must not treat that as a CLI command.
        var result = CliArgumentParser.Parse(new[] { "foo", "bar" });
        Assert.IsType<CliCommand.None>(result);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void Parse_HelpAliases_AllReturnHelp(string arg)
    {
        var result = CliArgumentParser.Parse(new[] { arg });
        Assert.IsType<CliCommand.Help>(result);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Parse_VersionAliases_AllReturnVersion(string arg)
    {
        var result = CliArgumentParser.Parse(new[] { arg });
        Assert.IsType<CliCommand.Version>(result);
    }

    [Fact]
    public void Parse_ListInstances_ReturnsListInstances()
    {
        var result = CliArgumentParser.Parse(new[] { "--list-instances" });
        Assert.IsType<CliCommand.ListInstances>(result);
    }

    [Fact]
    public void Parse_ListVersions_NoType_ReturnsListVersionsWithNullType()
    {
        var result = CliArgumentParser.Parse(new[] { "--list-versions" });
        var lv = Assert.IsType<CliCommand.ListVersions>(result);
        Assert.Null(lv.Type);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("snapshot")]
    [InlineData("all")]
    public void Parse_ListVersions_WithType_ParsesType(string type)
    {
        var result = CliArgumentParser.Parse(new[] { "--list-versions", "--type", type });
        var lv = Assert.IsType<CliCommand.ListVersions>(result);
        Assert.Equal(type, lv.Type);
    }

    [Fact]
    public void Parse_ListVersions_InvalidType_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--list-versions", "--type", "garbage" });
        var inv = Assert.IsType<CliCommand.Invalid>(result);
        Assert.Contains("garbage", inv.Reason);
    }

    [Fact]
    public void Parse_ListVersions_TypeMissingValue_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--list-versions", "--type" });
        Assert.IsType<CliCommand.Invalid>(result);
    }

    [Fact]
    public void Parse_Launch_WithId_ReturnsLaunch()
    {
        var result = CliArgumentParser.Parse(new[] { "--launch", "abc123" });
        var l = Assert.IsType<CliCommand.Launch>(result);
        Assert.Equal("abc123", l.InstanceId);
        Assert.Null(l.Username);
        Assert.Null(l.Server);
    }

    [Fact]
    public void Parse_Launch_WithUsernameAndServer_ParsesBoth()
    {
        var result = CliArgumentParser.Parse(new[]
        {
            "--launch", "abc123", "--username", "Steve", "--server", "mc.hypixel.net:25565",
        });
        var l = Assert.IsType<CliCommand.Launch>(result);
        Assert.Equal("abc123", l.InstanceId);
        Assert.Equal("Steve", l.Username);
        Assert.Equal("mc.hypixel.net:25565", l.Server);
    }

    [Fact]
    public void Parse_Launch_NoId_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--launch" });
        Assert.IsType<CliCommand.Invalid>(result);
    }

    [Fact]
    public void Parse_Launch_FlagInsteadOfId_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--launch", "--username", "Steve" });
        Assert.IsType<CliCommand.Invalid>(result);
    }

    [Fact]
    public void Parse_Launch_UsernameMissingValue_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--launch", "id", "--username" });
        Assert.IsType<CliCommand.Invalid>(result);
    }

    [Fact]
    public void Parse_Launch_UnknownSubFlag_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--launch", "id", "--bogus", "x" });
        Assert.IsType<CliCommand.Invalid>(result);
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsInvalid()
    {
        var result = CliArgumentParser.Parse(new[] { "--nope" });
        var inv = Assert.IsType<CliCommand.Invalid>(result);
        Assert.Contains("--nope", inv.Reason);
    }

    [Fact]
    public void Parse_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CliArgumentParser.Parse(null!));
    }
}
