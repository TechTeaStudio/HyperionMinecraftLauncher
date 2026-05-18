using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Java;

/// <summary>
/// Pure-function tests for <see cref="AdoptiumUrlBuilder.BuildBinaryUrl"/>. The endpoint shape
/// is documented at https://api.adoptium.net/q/swagger-ui/#/Binary/getBinary - we only project
/// the "latest GA JRE" path here, but the inputs (os, arch, feature version) are exactly the
/// strings the Adoptium API expects.
/// </summary>
public class AdoptiumUrlBuilderTests
{
    [Fact]
    public void Build_WindowsX64Java17_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java17, "windows", "x64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse",
            url);
    }

    [Fact]
    public void Build_LinuxAarch64Java21_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java21, "linux", "aarch64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/21/ga/linux/aarch64/jre/hotspot/normal/eclipse",
            url);
    }

    [Fact]
    public void Build_MacX64Java8_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java8, "mac", "x64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/8/ga/mac/x64/jre/hotspot/normal/eclipse",
            url);
    }

    [Fact]
    public void Build_WindowsX64Java21_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java21, "windows", "x64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse",
            url);
    }

    [Fact]
    public void Build_LinuxX64Java8_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java8, "linux", "x64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/8/ga/linux/x64/jre/hotspot/normal/eclipse",
            url);
    }

    [Fact]
    public void Build_MacAarch64Java21_BuildsExpectedUrl()
    {
        var url = AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java21, "mac", "aarch64");
        Assert.Equal(
            "https://api.adoptium.net/v3/binary/latest/21/ga/mac/aarch64/jre/hotspot/normal/eclipse",
            url);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Build_EmptyOs_Throws(string? os)
    {
        Assert.Throws<System.ArgumentException>(
            () => AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java21, os!, "x64"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Build_EmptyArch_Throws(string? arch)
    {
        Assert.Throws<System.ArgumentException>(
            () => AdoptiumUrlBuilder.BuildBinaryUrl(JavaRequirement.Java21, "windows", arch!));
    }
}
