namespace Taskitify.Core.Layout;

/// <summary>Rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public double CenterX => X + (Width / 2.0);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromLTRB(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public bool Contains(PixelRect other) =>
        other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

    public bool Intersects(PixelRect other) =>
        other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

    public bool VerticallyOverlaps(PixelRect other) => other.Top < Bottom && other.Bottom > Top;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

public static class OverlayLayout
{
    /// <summary>
    /// Computes the overlay rectangle: covers the widget completely and, when a wider overlay
    /// is wanted, grows away from the screen edge it is closest to (a widget on the right half
    /// of the taskbar grows left), stopping before any neighbouring taskbar button.
    /// </summary>
    public static PixelRect Compute(PixelRect widget, PixelRect taskbar, IReadOnlyList<PixelRect> obstacles, int desiredWidth, int margin)
    {
        var width = Math.Max(widget.Width, desiredWidth);
        var top = taskbar.Top;
        var height = taskbar.Height;

        var neighbours = obstacles
            .Where(o => !o.IsEmpty && o.VerticallyOverlaps(taskbar) && !o.Intersects(widget))
            .ToList();

        if (widget.CenterX > taskbar.CenterX)
        {
            var right = widget.Right;
            var limit = taskbar.Left;
            foreach (var o in neighbours.Where(o => o.Right <= widget.Left))
                limit = Math.Max(limit, o.Right + margin);
            var left = Math.Min(Math.Max(right - width, limit), widget.Left);
            return PixelRect.FromLTRB(left, top, right, top + height);
        }
        else
        {
            var left = widget.Left;
            var limit = taskbar.Right;
            foreach (var o in neighbours.Where(o => o.Left >= widget.Right))
                limit = Math.Min(limit, o.Left - margin);
            var right = Math.Max(Math.Min(left + width, limit), widget.Right);
            return PixelRect.FromLTRB(left, top, right, top + height);
        }
    }

    /// <summary>
    /// Calibration: <paramref name="from"/> with one side (-1 left, 1 right) moved by
    /// <paramref name="offset"/> pixels. The other side stays put, and the side stops at
    /// <paramref name="minWidth"/> instead of crossing it.
    /// </summary>
    public static PixelRect DragSide(PixelRect from, int edge, int offset, int minWidth)
    {
        if (edge < 0)
        {
            var left = Math.Min(from.Left + offset, from.Right - minWidth);
            return PixelRect.FromLTRB(left, from.Top, from.Right, from.Bottom);
        }
        var right = Math.Max(from.Right + offset, from.Left + minWidth);
        return PixelRect.FromLTRB(from.Left, from.Top, right, from.Bottom);
    }
}
