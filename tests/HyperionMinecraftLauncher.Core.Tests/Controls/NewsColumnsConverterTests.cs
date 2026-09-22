using System.Globalization;
using TechTeaStudio.HyperionMinecraftLauncher.App.Views;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Controls;

public class NewsColumnsConverterTests
{
    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(580.0, 1)]    // minimum window
    [InlineData(899.0, 1)]
    [InlineData(900.0, 2)]
    [InlineData(1650.0, 2)]   // maximized Full HD: two big cards per row, never a dense grid
    public void Convert_PicksOneOrTwoColumns(double width, int expected)
    {
        Assert.Equal(expected, NewsColumnsConverter.Instance.Convert(width, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
