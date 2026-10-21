using Taskitify.Core.Layout;

namespace Taskitify.Tests;

public class OverlayLayoutTests
{
    // Measured on a 1920x1200 screen with a left-aligned taskbar: widget sits next to the tray.
    private static readonly PixelRect Taskbar = new(0, 1152, 1920, 48);
    private static readonly PixelRect RightWidget = new(1415, 1152, 152, 48);

    [Fact]
    public void Right_side_widget_grows_left_into_free_space()
    {
        var lastAppIcon = new PixelRect(675, 1152, 44, 48);
        var tray = new PixelRect(1579, 1152, 32, 48);

        var r = OverlayLayout.Compute(RightWidget, Taskbar, [lastAppIcon, tray], desiredWidth: 300, margin: 4);

        Assert.Equal(PixelRect.FromLTRB(1567 - 300, 1152, 1567, 1200), r);
    }

    [Fact]
    public void Growth_stops_before_neighbour_button()
    {
        var icon = new PixelRect(1300, 1152, 44, 48);

        var r = OverlayLayout.Compute(RightWidget, Taskbar, [icon], desiredWidth: 400, margin: 4);

        Assert.Equal(1348, r.Left);
        Assert.Equal(RightWidget.Right, r.Right);
    }

    [Fact]
    public void Left_side_widget_grows_right()
    {
        var widget = new PixelRect(12, 1152, 160, 48);
        var centeredStart = new PixelRect(700, 1152, 44, 48);

        var r = OverlayLayout.Compute(widget, Taskbar, [centeredStart], desiredWidth: 1000, margin: 4);

        Assert.Equal(12, r.Left);
        Assert.Equal(696, r.Right);
    }

    [Fact]
    public void Never_smaller_than_the_widget()
    {
        var crowding = new PixelRect(1400, 1152, 14, 48); // touches widget
        var r = OverlayLayout.Compute(RightWidget, Taskbar, [crowding], desiredWidth: 50, margin: 4);

        Assert.True(r.Contains(RightWidget));
    }

    [Fact]
    public void Dragging_the_right_side_keeps_the_left_one()
    {
        var calibrated = new PixelRect(1000, 1152, 300, 48);

        Assert.Equal(new PixelRect(1000, 1152, 350, 48), OverlayLayout.DragSide(calibrated, edge: 1, offset: 50, minWidth: 40));
        Assert.Equal(new PixelRect(1000, 1152, 200, 48), OverlayLayout.DragSide(calibrated, edge: 1, offset: -100, minWidth: 40));
    }

    [Fact]
    public void Dragging_the_left_side_keeps_the_right_one()
    {
        var calibrated = new PixelRect(1000, 1152, 300, 48);

        Assert.Equal(PixelRect.FromLTRB(940, 1152, 1300, 1200), OverlayLayout.DragSide(calibrated, edge: -1, offset: -60, minWidth: 40));
        Assert.Equal(PixelRect.FromLTRB(1100, 1152, 1300, 1200), OverlayLayout.DragSide(calibrated, edge: -1, offset: 100, minWidth: 40));
    }

    [Fact]
    public void A_dragged_side_stops_at_the_minimum_width()
    {
        var calibrated = new PixelRect(1000, 1152, 300, 48);

        // Far past the other side: the strip keeps its minimum width and does not flip over.
        Assert.Equal(PixelRect.FromLTRB(1260, 1152, 1300, 1200), OverlayLayout.DragSide(calibrated, edge: -1, offset: 1000, minWidth: 40));
        Assert.Equal(new PixelRect(1000, 1152, 40, 48), OverlayLayout.DragSide(calibrated, edge: 1, offset: -1000, minWidth: 40));
    }
}
