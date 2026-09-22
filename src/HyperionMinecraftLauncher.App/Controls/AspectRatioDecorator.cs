using System;
using Avalonia;
using Avalonia.Controls;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

/// <summary>
/// Sizes its child to the available width and <c>width / Ratio</c> height, so a picture keeps
/// its proportions whatever the column width (news cards: minecraft.net key art is 1170x500).
/// With no width to work from it simply takes the child's own size.
/// </summary>
public sealed class AspectRatioDecorator : Decorator
{
    /// <summary>Width divided by height; values &lt;= 0 disable the constraint.</summary>
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioDecorator, double>(nameof(Ratio), 16.0 / 9.0);

    static AspectRatioDecorator()
    {
        AffectsMeasure<AspectRatioDecorator>(RatioProperty);
    }

    /// <inheritdoc cref="RatioProperty"/>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width) || Ratio <= 0)
            return base.MeasureOverride(availableSize);

        var size = new Size(availableSize.Width, availableSize.Width / Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
