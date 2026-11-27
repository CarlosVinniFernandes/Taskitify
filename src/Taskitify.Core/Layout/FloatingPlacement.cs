namespace Taskitify.Core.Layout;

/// <summary>
/// Where a free-floating window (Minimal Mode's lyrics) goes: where the user left it when that
/// place is still on a screen, otherwise somewhere sensible, so it never ends up out of reach
/// after a monitor is unplugged or the resolution changes.
/// </summary>
public static class FloatingPlacement
{
    /// <summary>How much of the window must stay on a screen for its saved place to count (px).</summary>
    public const int MinVisible = 48;

    /// <param name="saved">The saved bounds (empty: never placed).</param>
    /// <param name="workAreas">Every monitor's work area; the primary one first.</param>
    /// <param name="defaultSize">The size for a first placement.</param>
    public static PixelRect Resolve(PixelRect saved, IReadOnlyList<PixelRect> workAreas, (int Width, int Height) defaultSize)
    {
        if (workAreas.Count == 0)
            return saved.IsEmpty ? new PixelRect(0, 0, defaultSize.Width, defaultSize.Height) : saved;
        if (!saved.IsEmpty && workAreas.Any(area => VisiblePart(saved, area) is { Width: >= MinVisible, Height: >= MinVisible }))
            return saved;

        // A first placement, or a lost one: the primary screen, centred, a little above the taskbar.
        var primary = workAreas[0];
        var width = Math.Min(saved.IsEmpty ? defaultSize.Width : saved.Width, primary.Width);
        var height = Math.Min(saved.IsEmpty ? defaultSize.Height : saved.Height, primary.Height);
        var x = primary.X + ((primary.Width - width) / 2);
        var y = primary.Bottom - height - Math.Max(0, Math.Min(primary.Height / 8, primary.Height - height));
        return new PixelRect(x, y, width, height);
    }

    private static PixelRect VisiblePart(PixelRect r, PixelRect area)
    {
        var left = Math.Max(r.X, area.X);
        var top = Math.Max(r.Y, area.Y);
        var right = Math.Min(r.Right, area.Right);
        var bottom = Math.Min(r.Bottom, area.Bottom);
        return right > left && bottom > top ? PixelRect.FromLTRB(left, top, right, bottom) : default;
    }
}
