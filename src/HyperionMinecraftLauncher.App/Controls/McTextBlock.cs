using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// TextBlock that renders Minecraft-formatted text (<see cref="McTextSpan"/>s from a pack
/// description) as coloured / bold / italic / underlined / struck-through runs, the way the
/// game's pack screen does. Spans without a colour use the block's own Foreground.
/// </summary>
public sealed class McTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<McTextSpan>?> SpansProperty =
        AvaloniaProperty.Register<McTextBlock, IReadOnlyList<McTextSpan>?>(nameof(Spans));

    static McTextBlock()
    {
        SpansProperty.Changed.AddClassHandler<McTextBlock>((block, _) => block.Rebuild());
    }

    // Styles keyed on TextBlock apply to this control too.
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public IReadOnlyList<McTextSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var span in Spans ?? Array.Empty<McTextSpan>())
        {
            var run = new Run(span.Text);
            if (span.Color is { } hex && Color.TryParse(hex, out var color))
                run.Foreground = new SolidColorBrush(color);
            if (span.Bold) run.FontWeight = FontWeight.Bold;
            if (span.Italic) run.FontStyle = FontStyle.Italic;
            if (span.Underlined) run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            else if (span.Strikethrough) run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
            inlines.Add(run);
        }
        Inlines = inlines;
    }
}
