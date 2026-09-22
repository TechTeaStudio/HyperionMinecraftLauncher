using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Logging;

public class ClefLogFormatterTests
{
    // Fixed +03:00 zone so the expectations don't depend on the machine running the tests.
    private static readonly TimeZoneInfo Msk = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

    [Fact]
    public void FormatLine_InfoEvent_RendersLocalTimeLevelAndMessage()
    {
        const string line = """{"@t":"2026-09-22T03:59:06.4373799Z","@mt":"{Message}","Message":"Listing Minecraft versions ..."}""";

        Assert.Equal("06:59:06 INF Listing Minecraft versions ...", ClefLogFormatter.FormatLine(line, Msk));
    }

    [Theory]
    [InlineData("Warning", "WRN")]
    [InlineData("Error", "ERR")]
    [InlineData("Fatal", "FTL")]
    [InlineData("Debug", "DBG")]
    [InlineData("Verbose", "VRB")]
    public void FormatLine_Level_IsAbbreviated(string level, string expected)
    {
        var line = $$"""{"@t":"2026-09-22T03:59:06Z","@mt":"{Message}","@l":"{{level}}","Message":"m"}""";

        Assert.Equal($"06:59:06 {expected} m", ClefLogFormatter.FormatLine(line, Msk));
    }

    [Fact]
    public void FormatLine_Exception_IsAppendedIndentedOnFollowingLines()
    {
        const string line = """{"@t":"2026-09-22T03:59:06Z","@mt":"{Message}","@l":"Error","@x":"System.Exception: boom\r\n   at A.B()","Message":"Import failed."}""";

        Assert.Equal(
            "06:59:06 ERR Import failed." + Environment.NewLine +
            "    System.Exception: boom" + Environment.NewLine +
            "       at A.B()",
            ClefLogFormatter.FormatLine(line, Msk));
    }

    [Fact]
    public void FormatLine_NoMessageProperty_FallsBackToTemplate()
    {
        const string line = """{"@t":"2026-09-22T03:59:06Z","@mt":"Raw template text"}""";

        Assert.Equal("06:59:06 INF Raw template text", ClefLogFormatter.FormatLine(line, Msk));
    }

    [Theory]
    [InlineData("plain text line")]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("""{"no":"timestamp"}""")]
    public void FormatLine_NotClef_ReturnedUnchanged(string line)
    {
        Assert.Equal(line, ClefLogFormatter.FormatLine(line, Msk));
    }

    [Fact]
    public void FormatText_FormatsEveryLine()
    {
        var text = """{"@t":"2026-09-22T03:59:06Z","@mt":"{Message}","Message":"a"}""" + "\n" +
                   """{"@t":"2026-09-22T03:59:07Z","@mt":"{Message}","Message":"b"}""" + "\n";

        Assert.Equal(
            "06:59:06 INF a" + Environment.NewLine + "06:59:07 INF b",
            ClefLogFormatter.FormatText(text, Msk));
    }
}
