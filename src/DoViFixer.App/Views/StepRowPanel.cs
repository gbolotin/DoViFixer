using System.Windows;
using System.Windows.Controls;

namespace DoViFixer.App.Views;

/// <summary>
/// Lays out a stepper in one row as wide as the panel: the last step keeps its own width at the right end and the
/// other steps share the rest equally, so their connectors reach the last step.
/// </summary>
public sealed class StepRowPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        if (count == 0)
        {
            return default;
        }

        var last = InternalChildren[count - 1];
        last.Measure(availableSize);
        double share = count == 1 ? 0 : Math.Max(0, availableSize.Width - last.DesiredSize.Width) / (count - 1);
        double width = last.DesiredSize.Width;
        double height = last.DesiredSize.Height;
        for (int i = 0; i < count - 1; i++)
        {
            var step = InternalChildren[i];
            step.Measure(new Size(share, availableSize.Height));
            width += step.DesiredSize.Width;
            height = Math.Max(height, step.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var last = InternalChildren[count - 1];
        double lastWidth = Math.Min(last.DesiredSize.Width, finalSize.Width);
        double share = count == 1 ? 0 : (finalSize.Width - lastWidth) / (count - 1);
        for (int i = 0; i < count - 1; i++)
        {
            InternalChildren[i].Arrange(new Rect(i * share, 0, share, finalSize.Height));
        }

        last.Arrange(new Rect(finalSize.Width - lastWidth, 0, lastWidth, finalSize.Height));
        return finalSize;
    }
}
