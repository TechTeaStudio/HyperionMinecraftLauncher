using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances.Import.MultiMc;

/// <summary>
/// Exercise the tolerant INI parser for Prism's <c>instance.cfg</c>: real-world key/value
/// shapes, comment lines, quoted values, missing optional keys, malformed numbers, and
/// section headers from old MultiMC builds.
/// </summary>
public class MultiMcInstanceCfgParserTests
{
    [Fact]
    public void Parse_StandardEqualsSeparator_ReadsAllKnownKeys()
    {
        var raw = """
            InstanceType=OneSix
            name=My Modpack
            iconKey=flame
            JvmArgs=-XX:+UnlockExperimentalVMOptions -Dlog4j.skipJansi=false
            MaxMemAlloc=8192
            MinMemAlloc=1024
            notes=Some user notes
            ManagedPack=false
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("My Modpack", cfg.Name);
        Assert.Equal("flame", cfg.IconKey);
        Assert.Equal("-XX:+UnlockExperimentalVMOptions -Dlog4j.skipJansi=false", cfg.JvmArgs);
        Assert.Equal(1024, cfg.MinMemAllocMb);
        Assert.Equal(8192, cfg.MaxMemAllocMb);
        Assert.Equal("Some user notes", cfg.Notes);
    }

    [Fact]
    public void Parse_ColonSeparator_StillReadsKnownKeys()
    {
        // Some community variants emit YAML-style colon separators; our parser falls back to ':'.
        var raw = """
            name : Pack With Colons
            iconKey : grass
            MaxMemAlloc : 4096
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("Pack With Colons", cfg.Name);
        Assert.Equal("grass", cfg.IconKey);
        Assert.Equal(4096, cfg.MaxMemAllocMb);
    }

    [Fact]
    public void Parse_CommentLinesAreIgnored()
    {
        var raw = """
            # this is a hash comment
            ; this is a semicolon comment
            name=AfterComments
            # iconKey=should-not-be-read
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("AfterComments", cfg.Name);
        Assert.Null(cfg.IconKey);
    }

    [Fact]
    public void Parse_QuotedValues_StripsSurroundingQuotes()
    {
        var raw = """
            name="My Quoted Pack"
            JvmArgs='-Xss1M -DquietMode=true'
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("My Quoted Pack", cfg.Name);
        Assert.Equal("-Xss1M -DquietMode=true", cfg.JvmArgs);
    }

    [Fact]
    public void Parse_MissingOptionalKeys_LeavesFieldsNull()
    {
        var raw = """
            name=Minimal
            InstanceType=OneSix
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("Minimal", cfg.Name);
        Assert.Null(cfg.IconKey);
        Assert.Null(cfg.JvmArgs);
        Assert.Null(cfg.MinMemAllocMb);
        Assert.Null(cfg.MaxMemAllocMb);
        Assert.Null(cfg.Notes);
    }

    [Fact]
    public void Parse_MalformedNumber_LeavesMemoryFieldsNull()
    {
        var raw = """
            name=BadNumbers
            MaxMemAlloc=eight gigabytes
            MinMemAlloc=
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("BadNumbers", cfg.Name);
        Assert.Null(cfg.MaxMemAllocMb);
        Assert.Null(cfg.MinMemAllocMb);
    }

    [Fact]
    public void Parse_SectionHeader_IsIgnored()
    {
        // Old MultiMC builds (before Prism) emitted a [General] header.
        var raw = """
            [General]
            name=WithSection
            iconKey=flame
            """;

        var cfg = MultiMcInstanceCfgParser.Parse(raw);

        Assert.Equal("WithSection", cfg.Name);
        Assert.Equal("flame", cfg.IconKey);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsAllNull()
    {
        var cfg = MultiMcInstanceCfgParser.Parse(string.Empty);

        Assert.Null(cfg.Name);
        Assert.Null(cfg.IconKey);
        Assert.Null(cfg.JvmArgs);
        Assert.Null(cfg.MinMemAllocMb);
        Assert.Null(cfg.MaxMemAllocMb);
        Assert.Null(cfg.Notes);
    }
}
