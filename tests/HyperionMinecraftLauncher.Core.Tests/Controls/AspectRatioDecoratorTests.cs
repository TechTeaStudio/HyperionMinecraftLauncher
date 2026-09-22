using Avalonia;
using Avalonia.Controls;
using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Controls;

public class AspectRatioDecoratorTests
{
    [Fact]
    public void Measure_HeightFollowsAvailableWidth()
    {
        var box = new AspectRatioDecorator { Ratio = 2.34, Child = new Border() };

        box.Measure(new Size(468, double.PositiveInfinity));

        Assert.Equal(new Size(468, 200), box.DesiredSize);
    }

    [Fact]
    public void Arrange_GivesTheChildTheWholeBox()
    {
        var child = new Border();
        var box = new AspectRatioDecorator { Ratio = 2, Child = child };

        box.Measure(new Size(300, double.PositiveInfinity));
        box.Arrange(new Rect(box.DesiredSize));

        Assert.Equal(new Size(300, 150), child.Bounds.Size);
    }

    [Fact]
    public void Measure_UnboundedWidth_FallsBackToTheChild()
    {
        var box = new AspectRatioDecorator { Ratio = 2, Child = new Border { Width = 80, Height = 30 } };

        box.Measure(Size.Infinity);

        Assert.Equal(new Size(80, 30), box.DesiredSize);
    }
}
