using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Java;

/// <summary>
/// Pure-function tests for <see cref="JavaRequirementResolver.For"/>.
///
/// Cutoff rules (from Mojang's published JRE requirement bumps):
///   - 1.20.5 and later  -> Java 21
///   - 1.17 .. 1.20.4    -> Java 17
///   - 1.16.x and older  -> Java 8
///
/// Snapshot ids (e.g. "23w14a") are mapped to the nearest known release in the same year/season.
/// </summary>
public class JavaRequirementResolverTests
{
    [Theory]
    // Java 21 era
    [InlineData("1.21.5", JavaRequirement.Java21)]
    [InlineData("1.21", JavaRequirement.Java21)]
    [InlineData("1.20.5", JavaRequirement.Java21)]
    [InlineData("1.20.6", JavaRequirement.Java21)]
    // Java 17 era
    [InlineData("1.20.4", JavaRequirement.Java17)]
    [InlineData("1.20", JavaRequirement.Java17)]
    [InlineData("1.19.4", JavaRequirement.Java17)]
    [InlineData("1.18.2", JavaRequirement.Java17)]
    [InlineData("1.17.1", JavaRequirement.Java17)]
    [InlineData("1.17", JavaRequirement.Java17)]
    // Java 8 era
    [InlineData("1.16.5", JavaRequirement.Java8)]
    [InlineData("1.16", JavaRequirement.Java8)]
    [InlineData("1.12.2", JavaRequirement.Java8)]
    [InlineData("1.8.9", JavaRequirement.Java8)]
    [InlineData("1.7.10", JavaRequirement.Java8)]
    public void For_ReleaseVersions_MapsToExpectedJava(string mcVersion, JavaRequirement expected)
    {
        Assert.Equal(expected, JavaRequirementResolver.For(mcVersion));
    }

    [Theory]
    // 24wXX -> 1.20.5+ (Java 21).  1.20.5 was 24w14a-era.
    [InlineData("24w14a", JavaRequirement.Java21)]
    [InlineData("24w33a", JavaRequirement.Java21)]
    // 23wXX maps to 1.20.x cycle -> Java 17 (1.20.4 and below).
    [InlineData("23w14a", JavaRequirement.Java17)]
    [InlineData("23w13a_or_b", JavaRequirement.Java17)]
    // 22wXX maps to 1.19.x -> Java 17.
    [InlineData("22w43a", JavaRequirement.Java17)]
    // 21wXX maps to 1.18 cycle -> Java 17 (1.17 started here).
    [InlineData("21w03a", JavaRequirement.Java17)]
    // Older snapshots fall back to Java 8.
    [InlineData("20w14a", JavaRequirement.Java8)]
    [InlineData("19w34a", JavaRequirement.Java8)]
    [InlineData("16w20a", JavaRequirement.Java8)]
    public void For_Snapshots_MapsToClosestRelease(string mcVersion, JavaRequirement expected)
    {
        Assert.Equal(expected, JavaRequirementResolver.For(mcVersion));
    }

    [Theory]
    // Pre-release / release-candidate suffixes pass through to the base release.
    [InlineData("1.20.5-rc1", JavaRequirement.Java21)]
    [InlineData("1.20.5-pre1", JavaRequirement.Java21)]
    [InlineData("1.17-pre3", JavaRequirement.Java17)]
    [InlineData("1.16.5-rc1", JavaRequirement.Java8)]
    public void For_PreReleaseSuffixes_UseUnderlyingRelease(string mcVersion, JavaRequirement expected)
    {
        Assert.Equal(expected, JavaRequirementResolver.For(mcVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    public void For_NullOrUnparseable_FallsBackToJava21(string? mcVersion)
    {
        // When we genuinely don't recognise the version, assume the user is on the bleeding edge
        // and downloading Java 21 is the safest default. The launch will still work for older
        // versions on Java 21 in practice (Mojang's bundled-runtime story is more strict).
        Assert.Equal(JavaRequirement.Java21, JavaRequirementResolver.For(mcVersion!));
    }
}
