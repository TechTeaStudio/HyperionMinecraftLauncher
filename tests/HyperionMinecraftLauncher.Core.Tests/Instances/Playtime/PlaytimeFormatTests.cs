using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Playtime;

public class PlaytimeFormatTests
{
    private const string HoursMinutes = "{0} h {1} min";
    private const string Minutes = "{0} min";

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(59 * 60 + 59, "59 min")]
    [InlineData(60 * 60, "1 h 0 min")]
    [InlineData(61 * 60, "1 h 1 min")]
    [InlineData(125 * 3600 + 5 * 60 + 30, "125 h 5 min")]
    public void Format_PicksMinutesOrHoursAndMinutes(int seconds, string expected)
    {
        Assert.Equal(expected, PlaytimeFormat.Format(TimeSpan.FromSeconds(seconds), HoursMinutes, Minutes));
    }

    [Fact]
    public void Format_Negative_IsTreatedAsZero()
    {
        Assert.Equal("0 min", PlaytimeFormat.Format(TimeSpan.FromMinutes(-3), HoursMinutes, Minutes));
    }
}
