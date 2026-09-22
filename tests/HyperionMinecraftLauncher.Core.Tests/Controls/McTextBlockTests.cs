using System.Linq;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Controls;

public class McTextBlockTests
{
    [Fact]
    public void Spans_BecomeStyledRuns()
    {
        var block = new McTextBlock
        {
            Spans = new[]
            {
                new McTextSpan("Version 9.5 ", "#00AA00", false, false, false, false),
                new McTextSpan("BETA", "#AA0000", true, true, true, false),
                new McTextSpan(" plain", null, false, false, false, true),
            },
        };

        var runs = block.Inlines!.OfType<Run>().ToArray();

        Assert.Equal(new[] { "Version 9.5 ", "BETA", " plain" }, runs.Select(r => r.Text));
        Assert.Equal(Color.Parse("#00AA00"), ((ISolidColorBrush)runs[0].Foreground!).Color);
        Assert.Equal((FontWeight.Bold, FontStyle.Italic), (runs[1].FontWeight, runs[1].FontStyle));
        Assert.Same(TextDecorations.Underline, runs[1].TextDecorations);
        Assert.False(runs[2].IsSet(TextElement.ForegroundProperty)); // default colour comes from the TextBlock
        Assert.Same(TextDecorations.Strikethrough, runs[2].TextDecorations);
    }

    [Fact]
    public void ClearingSpans_ClearsTheText()
    {
        var block = new McTextBlock { Spans = new[] { new McTextSpan("x", null, false, false, false, false) } };

        block.Spans = null;

        Assert.Empty(block.Inlines!);
    }
}
