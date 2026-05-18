using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Launcher;

/// <summary>
/// <see cref="CmlLibMinecraftLauncherService.ParseHostPort"/> is the central host:port splitter
/// used by the Servers "Join" path. Servers.dat stores the IP as a free-form string; we accept
/// hostname, hostname:port, IPv4:port, and bracketed IPv6.
/// </summary>
public class ParseHostPortTests
{
    [Theory]
    [InlineData("mc.hypixel.net", "mc.hypixel.net", 25565)]
    [InlineData("play.example.com:25577", "play.example.com", 25577)]
    [InlineData("127.0.0.1:19132", "127.0.0.1", 19132)]
    [InlineData("[::1]:25565", "::1", 25565)]
    [InlineData("[2001:db8::1]:5000", "2001:db8::1", 5000)]
    [InlineData("[::1]", "::1", 25565)]
    [InlineData("  mc.example.com:25565  ", "mc.example.com", 25565)]
    public void Parses(string input, string expectedHost, int expectedPort)
    {
        var (host, port) = CmlLibMinecraftLauncherService.ParseHostPort(input);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("mc.hypixel.net:")] // trailing colon -> default
    [InlineData("mc.hypixel.net:abc")] // garbage port -> default
    [InlineData("mc.hypixel.net:0")] // out-of-range -> default
    [InlineData("mc.hypixel.net:70000")] // out-of-range -> default
    public void GarbagePortFallsBackToDefault(string input)
    {
        var (_, port) = CmlLibMinecraftLauncherService.ParseHostPort(input);
        Assert.Equal(25565, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputThrows(string? input)
    {
        Assert.Throws<ArgumentException>(() => CmlLibMinecraftLauncherService.ParseHostPort(input!));
    }
}
