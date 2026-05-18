using TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Updates;

public class SemverComparerTests
{
    [Theory]
    // Plain X.Y.Z comparisons.
    [InlineData("0.27.0",  "0.26.5",  true)]
    [InlineData("0.27.0",  "0.27.0",  false)] // Equal returns false.
    [InlineData("0.26.5",  "0.27.0",  false)] // Older returns false.
    [InlineData("1.0.0",   "0.99.99", true)]
    [InlineData("0.27.10", "0.27.9",  true)]  // Numeric (not lexicographic) compare on patch.
    // Leading "v" stripped on either side.
    [InlineData("v0.28.0", "0.27.9",  true)]
    [InlineData("0.28.0",  "v0.27.9", true)]
    [InlineData("v0.28.0", "v0.28.0", false)]
    // Malformed inputs return false (never throw, never claim "newer").
    [InlineData("not-a-version", "0.27.0", false)]
    [InlineData("0.27.0", "not-a-version", false)]
    [InlineData("",       "0.27.0", false)]
    [InlineData("0.27.0", "",       false)]
    [InlineData("0.27",   "0.27.0", false)] // Strict X.Y.Z requirement.
    public void IsNewerThan_MatchesTable(string a, string b, bool expected)
    {
        Assert.Equal(expected, SemverComparer.IsNewerThan(a, b));
    }
}
