using System.Linq;
using System.Text.Json;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.InstanceBrowsing;

// Fixtures are the real pack.mcmeta descriptions of the packs in the Nestle Optimized modpack.
public class McTextTests
{
    private static McTextSpan[] Parse(string json) => McText.FromJson(JsonDocument.Parse(json).RootElement).ToArray();

    [Fact]
    public void LegacyCodes_SplitIntoColouredSpans()
    {
        var spans = Parse("\"§2Version 9.5 §aVanilla Edition\\n§e©Motschen 2026 | MIT Licence\"");

        Assert.Equal(
            new (string, string?)[] { ("Version 9.5 ", "#00AA00"), ("Vanilla Edition\n", "#55FF55"), ("©Motschen 2026 | MIT Licence", "#FFFF55") },
            spans.Select(s => (s.Text, s.Color)).ToArray());
    }

    [Fact]
    public void TranslateWithFallback_ShowsTheFallback_AndResetDropsTheColour()
    {
        var spans = Parse("""
            {"translate":"fo.resourcePack.fastbettergrass","fallback":"OptiFine's Better Grass (fast)§r\n§4Must be the first to work"}
            """);

        Assert.Equal(
            new[] { ("OptiFine's Better Grass (fast)\n", (string?)null), ("Must be the first to work", "#AA0000") },
            spans.Select(s => (s.Text, s.Color)).ToArray());
    }

    [Fact]
    public void ComponentArray_KeepsHexColours_AndMergesEqualNeighbours()
    {
        var spans = Parse("""
            [{"text":" H","color":"#e05400"},{"text":"a","color":"#e35b00"},{"text":"nd","color":"#e35b00"},
             {"text":"le","color":"gold","bold":true,"extra":[{"text":"!","italic":true}]}]
            """);

        Assert.Equal(4, spans.Length);
        Assert.Equal((" H", "#E05400"), (spans[0].Text, spans[0].Color));
        Assert.Equal(("and", "#E35B00"), (spans[1].Text, spans[1].Color)); // merged "a" + "nd"
        Assert.Equal(("le", "#FFAA00", true, false), (spans[2].Text, spans[2].Color, spans[2].Bold, spans[2].Italic));
        Assert.Equal(("!", "#FFAA00", true, true), (spans[3].Text, spans[3].Color, spans[3].Bold, spans[3].Italic)); // extra inherits
    }

    [Fact]
    public void FormattingCodes_AreTrackedUntilTheNextColourOrReset()
    {
        var spans = Parse("\"§lBold §oboth §cred §kobf§r plain\"");

        Assert.Equal(
            new[]
            {
                ("Bold ", (string?)null, true, false),
                ("both ", null, true, true),
                ("red obf", "#FF5555", false, false), // a colour code clears bold/italic; §k is ignored
                (" plain", null, false, false),
            },
            spans.Select(s => (s.Text, s.Color, s.Bold, s.Italic)).ToArray());
    }

    [Fact]
    public void PlainString_IsOneUnstyledSpan()
    {
        var span = Assert.Single(Parse("\"Just text\""));

        Assert.Equal(("Just text", (string?)null, false), (span.Text, span.Color, span.Bold));
    }

    [Fact]
    public void ToPlainText_StripsAllFormatting()
    {
        Assert.Equal("Version 9.5 Vanilla Edition", McText.ToPlainText(Parse("\"§2Version 9.5 §aVanilla Edition\"")));
    }
}
