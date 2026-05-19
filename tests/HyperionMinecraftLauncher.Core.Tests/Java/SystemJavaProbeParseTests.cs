using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Java;

/// <summary>
/// Pure-function tests for <see cref="SystemJavaProbe.ParseMajor"/>. The strings tested here
/// are real <c>java -version</c> output samples from Eclipse Temurin, Microsoft OpenJDK,
/// Amazon Corretto, and the legacy Oracle Java 8 release line. If a vendor adds a new
/// version-banner shape we'll add a fixture here.
/// </summary>
public class SystemJavaProbeParseTests
{
    [Fact]
    public void ParseMajor_Java21Temurin_Returns21()
    {
        // Real Eclipse Temurin 21.0.2 stderr.
        var banner = "openjdk version \"21.0.2\" 2024-01-16 LTS\n" +
                     "OpenJDK Runtime Environment Temurin-21.0.2+13 (build 21.0.2+13-LTS)\n" +
                     "OpenJDK 64-Bit Server VM Temurin-21.0.2+13 (build 21.0.2+13-LTS, mixed mode, sharing)";
        Assert.Equal(21, SystemJavaProbe.ParseMajor(banner));
    }

    [Fact]
    public void ParseMajor_Java17MicrosoftOpenJdk_Returns17()
    {
        var banner = "openjdk version \"17.0.10\" 2024-01-16 LTS\n" +
                     "Microsoft OpenJDK Build 17.0.10+7-LTS\n" +
                     "OpenJDK 64-Bit Server VM Microsoft-9091332 (build 17.0.10+7-LTS, mixed mode, sharing)";
        Assert.Equal(17, SystemJavaProbe.ParseMajor(banner));
    }

    [Fact]
    public void ParseMajor_LegacyJava8_Returns8()
    {
        // Oracle / Adoptium Java 8 still reports the pre-JEP-223 "1.x" format.
        var banner = "java version \"1.8.0_311\"\n" +
                     "Java(TM) SE Runtime Environment (build 1.8.0_311-b11)\n" +
                     "Java HotSpot(TM) 64-Bit Server VM (build 25.311-b11, mixed mode)";
        Assert.Equal(8, SystemJavaProbe.ParseMajor(banner));
    }

    [Fact]
    public void ParseMajor_Java11NoMinor_Returns11()
    {
        // Some early JEP-223 era taglines drop the minor entirely.
        var banner = "openjdk version \"11\" 2018-09-25\n" +
                     "OpenJDK Runtime Environment 18.9 (build 11+28)\n" +
                     "OpenJDK 64-Bit Server VM 18.9 (build 11+28, mixed mode)";
        Assert.Equal(11, SystemJavaProbe.ParseMajor(banner));
    }

    [Fact]
    public void ParseMajor_GraalVMJava21_Returns21()
    {
        var banner = "openjdk version \"21.0.1\" 2023-10-17\n" +
                     "OpenJDK Runtime Environment GraalVM CE 21.0.1+12.1 (build 21.0.1+12-jvmci-23.1-b19)\n" +
                     "OpenJDK 64-Bit Server VM GraalVM CE 21.0.1+12.1 (build 21.0.1+12-jvmci-23.1-b19, mixed mode, sharing)";
        Assert.Equal(21, SystemJavaProbe.ParseMajor(banner));
    }

    [Fact]
    public void ParseMajor_EmptyString_ReturnsNull() =>
        Assert.Null(SystemJavaProbe.ParseMajor(string.Empty));

    [Fact]
    public void ParseMajor_NullString_ReturnsNull() =>
        Assert.Null(SystemJavaProbe.ParseMajor(null!));

    [Fact]
    public void ParseMajor_BannerWithoutVersionWord_ReturnsNull() =>
        Assert.Null(SystemJavaProbe.ParseMajor("totally unrelated output"));

    [Fact]
    public void ParseMajor_LegacyMissingMinor_ReturnsNull()
    {
        // A bare "1" with no minor is meaningless under the legacy scheme - reject rather
        // than guess. This is a synthetic, near-impossible banner included as a regression
        // guard against an accidental "return 1" if anyone refactors the legacy branch.
        var banner = "java version \"1\"";
        Assert.Null(SystemJavaProbe.ParseMajor(banner));
    }
}
