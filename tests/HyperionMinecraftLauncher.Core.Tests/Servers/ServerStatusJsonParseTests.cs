using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers;

/// <summary>
/// Verifies the JSON-shape parser against a representative 1.20.4 vanilla server status
/// response. We sanity-check the version description, player counts, the formatted MOTD
/// (section-sign codes stripped), and that a base64 favicon survives the round trip.
/// </summary>
public class ServerStatusJsonParseTests
{
    private const string SampleStatus = """
    {
        "version": { "name": "1.20.4", "protocol": 765 },
        "players": {
            "max": 100,
            "online": 7,
            "sample": [ { "name": "Notch", "id": "069a79f4-44e9-4726-a5be-fca90e38aaf5" } ]
        },
        "description": { "text": "§aHyperion§r Test Server" },
        "favicon": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkAAIAAAoAAv/lxKUAAAAASUVORK5CYII=",
        "enforcesSecureChat": true
    }
    """;

    [Fact]
    public void Parse_ExtractsVersionAndProtocol()
    {
        var status = ServerStatusJson.Parse(SampleStatus, latencyMs: 42);
        Assert.NotNull(status);
        Assert.Equal("1.20.4", status!.Version);
        Assert.Equal(765, status.Protocol);
    }

    [Fact]
    public void Parse_ExtractsPlayerCounts()
    {
        var status = ServerStatusJson.Parse(SampleStatus, latencyMs: 42);
        Assert.NotNull(status);
        Assert.Equal(7, status!.OnlinePlayers);
        Assert.Equal(100, status.MaxPlayers);
    }

    [Fact]
    public void Parse_StripsSectionSignFormattingFromMotd()
    {
        var status = ServerStatusJson.Parse(SampleStatus, latencyMs: 42);
        Assert.NotNull(status);
        Assert.Equal("Hyperion Test Server", status!.Motd);
    }

    [Fact]
    public void Parse_DecodesFaviconPng()
    {
        var status = ServerStatusJson.Parse(SampleStatus, latencyMs: 42);
        Assert.NotNull(status);
        Assert.NotNull(status!.FaviconPng);
        // First eight bytes of any PNG file are the magic signature.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, status.FaviconPng![..8]);
    }

    [Fact]
    public void Parse_PreservesLatencyArgument()
    {
        var status = ServerStatusJson.Parse(SampleStatus, latencyMs: 123);
        Assert.NotNull(status);
        Assert.Equal(123, status!.LatencyMs);
    }

    [Fact]
    public void Parse_HandlesPlainStringDescription()
    {
        const string body = """
        { "version": { "name": "1.8.9", "protocol": 47 },
          "players": { "max": 20, "online": 0 },
          "description": "A Minecraft Server" }
        """;
        var status = ServerStatusJson.Parse(body, latencyMs: 0);
        Assert.NotNull(status);
        Assert.Equal("A Minecraft Server", status!.Motd);
        Assert.Equal(47, status.Protocol);
    }

    [Fact]
    public void Parse_HandlesExtraComponentsInDescription()
    {
        const string body = """
        { "version": { "name": "1.20", "protocol": 763 },
          "players": { "max": 20, "online": 0 },
          "description": {
              "text": "Welcome",
              "extra": [ { "text": " to " }, { "text": "Hyperion", "color": "green" } ]
          } }
        """;
        var status = ServerStatusJson.Parse(body, latencyMs: 0);
        Assert.NotNull(status);
        Assert.Equal("Welcome to Hyperion", status!.Motd);
    }

    [Fact]
    public void Parse_ReturnsNullOnGarbage()
    {
        Assert.Null(ServerStatusJson.Parse("not json", latencyMs: 0));
        Assert.Null(ServerStatusJson.Parse("{}", latencyMs: 0));
    }
}
