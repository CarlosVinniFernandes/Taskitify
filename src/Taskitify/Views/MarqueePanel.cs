using System.Windows;
using System.Windows.Controls;

namespace Taskitify.Views;

/// <summary>
/// Stacks its children on top of each other (like a single-cell Grid). When
/// <see cref="Unbounded"/> is set, children get their full natural width instead of being
/// clipped to the panel, so a TranslateTransform can scroll them; the panel itself clips.
/// (A Grid would apply a layout clip that moves together with the child's transform.)
/// </summary>
public sealed class MarqueePanel : Panel
{
    private bool _unbounded;

    public MarqueePanel() => ClipToBounds = true;

    public bool Unbounded
    {
        get => _unbounded;
        set
        {
            if (_unbounded == value)
                return;
            _unbounded = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var childAvailable = _unbounded ? new Size(double.PositiveInfinity, availableSize.Height) : availableSize;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childAvailable);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var w = _unbounded ? Math.Max(child.DesiredSize.Width, finalSize.Width) : finalSize.Width;
            child.Arrange(new Rect(0, 0, w, finalSize.Height));
        }
        return finalSize;
    }
}
