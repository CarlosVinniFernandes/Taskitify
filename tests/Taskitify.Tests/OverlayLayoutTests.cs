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
}
