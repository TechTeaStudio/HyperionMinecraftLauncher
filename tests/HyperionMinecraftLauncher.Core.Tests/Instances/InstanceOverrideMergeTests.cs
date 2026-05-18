using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances;

/// <summary>
/// Tests for <see cref="InstanceLaunchSettings.Merge"/>: instance-side overrides win when
/// set (non-null int &gt; 0 / non-blank string), else the global settings value is used.
/// 8 cases covering empty / partial / complete / blank-vs-null semantics.
/// </summary>
public class InstanceOverrideMergeTests
{
    private static LauncherSettings DefaultSettings() => new()
    {
        MinimumRamMb = 1024,
        MaximumRamMb = 4096,
        JvmArguments = "-XX:+UseG1GC",
        GameDirectory = @"C:\global\minecraft",
    };

    private static Instance NewInstance(
        int? minRam = null,
        int? maxRam = null,
        string? jvm = null,
        string? gameDir = null,
        int? width = null,
        int? height = null) =>
        new()
        {
            Id = "i1",
            Name = "Test instance",
            VersionId = "1.21.5",
            MinimumRamMb = minRam,
            MaximumRamMb = maxRam,
            JvmArguments = jvm,
            GameDirectory = gameDir,
            ResolutionWidth = width,
            ResolutionHeight = height,
        };

    [Fact]
    public void Merge_NullInstance_ReturnsGlobalSettings()
    {
        var merged = InstanceLaunchSettings.Merge(null, DefaultSettings());

        Assert.Equal(1024, merged.MinimumRamMb);
        Assert.Equal(4096, merged.MaximumRamMb);
        Assert.Equal("-XX:+UseG1GC", merged.JvmArguments);
        Assert.Equal(@"C:\global\minecraft", merged.GameDirectory);
        Assert.Null(merged.ResolutionWidth);
        Assert.Null(merged.ResolutionHeight);
    }

    [Fact]
    public void Merge_InstanceWithNoOverrides_InheritsAllGlobals()
    {
        var merged = InstanceLaunchSettings.Merge(NewInstance(), DefaultSettings());

        Assert.Equal(1024, merged.MinimumRamMb);
        Assert.Equal(4096, merged.MaximumRamMb);
        Assert.Equal("-XX:+UseG1GC", merged.JvmArguments);
        Assert.Equal(@"C:\global\minecraft", merged.GameDirectory);
        Assert.Null(merged.ResolutionWidth);
        Assert.Null(merged.ResolutionHeight);
    }

    [Fact]
    public void Merge_InstanceOverridesEverything_GlobalsIgnored()
    {
        var inst = NewInstance(
            minRam: 2048,
            maxRam: 8192,
            jvm: "-Xss2m",
            gameDir: @"D:\custom\mc",
            width: 1920,
            height: 1080);

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal(2048, merged.MinimumRamMb);
        Assert.Equal(8192, merged.MaximumRamMb);
        Assert.Equal("-Xss2m", merged.JvmArguments);
        Assert.Equal(@"D:\custom\mc", merged.GameDirectory);
        Assert.Equal(1920, merged.ResolutionWidth);
        Assert.Equal(1080, merged.ResolutionHeight);
    }

    [Fact]
    public void Merge_InstanceOverridesRamOnly_OtherFieldsInherit()
    {
        var inst = NewInstance(minRam: 2048, maxRam: 6144);

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal(2048, merged.MinimumRamMb);
        Assert.Equal(6144, merged.MaximumRamMb);
        Assert.Equal("-XX:+UseG1GC", merged.JvmArguments);
        Assert.Equal(@"C:\global\minecraft", merged.GameDirectory);
        Assert.Null(merged.ResolutionWidth);
        Assert.Null(merged.ResolutionHeight);
    }

    [Fact]
    public void Merge_BlankJvmString_TreatedAsInheritNotEmpty()
    {
        var inst = NewInstance(jvm: "   ");

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal("-XX:+UseG1GC", merged.JvmArguments);
    }

    [Fact]
    public void Merge_EmptyJvmString_TreatedAsInherit()
    {
        var inst = NewInstance(jvm: string.Empty);

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal("-XX:+UseG1GC", merged.JvmArguments);
    }

    [Fact]
    public void Merge_BlankGameDirString_TreatedAsInherit()
    {
        var inst = NewInstance(gameDir: "   ");

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal(@"C:\global\minecraft", merged.GameDirectory);
    }

    [Fact]
    public void Merge_ZeroRamFields_TreatedAsInherit()
    {
        // The dialog binds NumericUpDown to int? but a user could plausibly land at 0 by
        // typing then deleting. Zero is not a valid heap size, so we coerce to "inherit".
        var inst = NewInstance(minRam: 0, maxRam: 0, width: 0, height: 0);

        var merged = InstanceLaunchSettings.Merge(inst, DefaultSettings());

        Assert.Equal(1024, merged.MinimumRamMb);
        Assert.Equal(4096, merged.MaximumRamMb);
        Assert.Null(merged.ResolutionWidth);
        Assert.Null(merged.ResolutionHeight);
    }

    [Fact]
    public void Merge_MaxLowerThanMin_ClampsMaxUpToMin()
    {
        // Instance overrides min above the global max - the JVM rejects Xmx < Xms, so the
        // merge bumps max up to match. (This is the only "clamp" the helper does.)
        var inst = NewInstance(minRam: 8192);
        var settings = DefaultSettings(); // global max = 4096

        var merged = InstanceLaunchSettings.Merge(inst, settings);

        Assert.Equal(8192, merged.MinimumRamMb);
        Assert.Equal(8192, merged.MaximumRamMb);
    }
}
