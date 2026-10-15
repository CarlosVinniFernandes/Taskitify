using System.Windows.Automation;
using Microsoft.Win32;
using Taskitify.Core.Layout;
using Taskitify.Infrastructure;
using Taskitify.Models;

namespace Taskitify.Services;

public sealed record TaskbarPlacement(
    PixelRect Taskbar,
    PixelRect Monitor,
    PixelRect Widget,
    bool WidgetDetected,
    IReadOnlyList<PixelRect> Obstacles,
    IntPtr TrayHandle,
    double Scale,
    bool AutoHide,
    string MonitorName);

/// <summary>
/// Finds where the Windows 11 weather/widgets button sits on the taskbar.
///
/// There is no public API for this. The taskbar is XAML hosted inside Explorer, and the button
/// is exposed through UI Automation with AutomationId "WidgetsButton" (verified on Windows 11
/// 23H2/24H2/25H2 builds). We read its BoundingRectangle (physical pixels, since the process is
/// per-monitor DPI aware) plus the rectangles of the other taskbar buttons, so the overlay
/// never covers them. If the button is not found (widgets disabled, a different build), an
/// estimate is used and the user can calibrate manually.
///
/// UIA calls are cross-process and comparatively slow, so callers run this off the UI thread.
/// </summary>
public sealed class TaskbarPositionService
{
    private const string WidgetsAutomationId = "WidgetsButton";

    public TaskbarPlacement? GetPlacement(string? monitorDeviceName)
    {
        var tray = FindTray(monitorDeviceName);
        if (tray == IntPtr.Zero)
            return null;

        var taskbar = WindowInterop.GetBounds(tray);
        var monitor = WindowInterop.MonitorForWindow(tray);
        if (taskbar.IsEmpty || monitor is null)
            return null;

        var scale = WindowInterop.GetScale(tray);
        var autoHide = WindowInterop.IsTaskbarAutoHide();
        if (autoHide)
        {
            // While hidden the window slides off-screen; lay out against its shown position.
            taskbar = taskbar with { Y = monitor.Bounds.Bottom - taskbar.Height };
        }

        PixelRect? widget = null;
        var obstacles = new List<PixelRect>();
        try
        {
            var root = AutomationElement.FromHandle(tray);
            var cache = new CacheRequest();
            cache.Add(AutomationElement.AutomationIdProperty);
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            cache.TreeFilter = Automation.RawViewCondition;
            using (cache.Activate())
            {
                var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement b in buttons)
                {
                    var rect = b.Cached.BoundingRectangle;
                    if (rect.IsEmpty || b.Cached.IsOffscreen)
                        continue;
                    var r = new PixelRect((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
                    if (r.IsEmpty)
                        continue;
                    if (b.Cached.AutomationId == WidgetsAutomationId)
                        widget = r;
                    else
                        obstacles.Add(r);
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"UI Automation query failed: {ex.Message}");
        }

        var detected = widget is not null;
        widget ??= EstimateWidget(taskbar, obstacles, scale);

        if (autoHide)
            widget = widget.Value with { Y = taskbar.Y };

        return new TaskbarPlacement(taskbar, monitor.Bounds, widget.Value, detected, obstacles, tray, scale, autoHide, monitor.DeviceName);
    }

    /// <summary>The overlay rectangle in physical pixels for the given placement and settings.</summary>
    /// <param name="compact">Minimized (paused) player: just covers the weather widget.</param>
    public static PixelRect ComputeOverlayRect(TaskbarPlacement p, AppSettings s, bool compact = false)
    {
        if (s.ManualPlacement && s.ManualWidth > 0 && s.ManualHeight > 0)
            return new PixelRect(s.ManualX, s.ManualY, s.ManualWidth, s.ManualHeight);

        // Each layout's width is for cover + text; chosen controls add their own room. Without
        // any text the strip is just cover + buttons. Never narrower than the weather widget.
        var layout = s.Layout(compact);
        var content = layout.AnyText ? layout.Width : (layout.ShowCover ? 46 : 8);
        var desired = (int)Math.Round((content + ControlsWidth(layout)) * p.Scale);
        return OverlayLayout.Compute(p.Widget, p.Taskbar, p.Obstacles, desired, (int)Math.Round(6 * p.Scale));
    }

    /// <summary>DIP width taken by the selected taskbar buttons (see TaskbarOverlay.xaml).</summary>
    public static int ControlsWidth(BarLayout s)
    {
        const int button = 28, play = 28 + 6, panelMargin = 8;
        var count = new[] { s.ShowShuffle, s.ShowPrevious, s.ShowNext, s.ShowRepeat, s.ShowVolume }.Count(b => b);
        var width = (count * button) + (s.ShowPlayPause ? play : 0);
        return width == 0 ? 0 : width + panelMargin;
    }

    /// <summary>True when the taskbar is actually on screen (auto-hide can slide it away).</summary>
    public static bool IsTaskbarShown(TaskbarPlacement p)
    {
        if (!NativeMethods.IsWindowVisible(p.TrayHandle))
            return false;
        if (!p.AutoHide)
            return true;
        var now = WindowInterop.GetBounds(p.TrayHandle);
        var visible = Math.Min(now.Bottom, p.Monitor.Bottom) - Math.Max(now.Top, p.Monitor.Top);
        return visible >= now.Height * 0.9;
    }

    public static IReadOnlyList<MonitorDetails> GetMonitors() =>
        System.Windows.Forms.Screen.AllScreens
            .Select(s => new MonitorDetails(IntPtr.Zero, s.DeviceName,
                new PixelRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height),
                new PixelRect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height),
                s.Primary))
            .ToList();

    private static IntPtr FindTray(string? monitorDeviceName)
    {
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (string.IsNullOrEmpty(monitorDeviceName))
            return primary;

        foreach (var tray in EnumerateTrays(primary))
        {
            if (WindowInterop.MonitorForWindow(tray)?.DeviceName.Equals(monitorDeviceName, StringComparison.OrdinalIgnoreCase) == true)
                return tray;
        }
        return primary;
    }

    private static IEnumerable<IntPtr> EnumerateTrays(IntPtr primary)
    {
        if (primary != IntPtr.Zero)
            yield return primary;
        var hwnd = IntPtr.Zero;
        while ((hwnd = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return hwnd;
    }

    /// <summary>
    /// Best guess when UIA does not expose the widget: centred taskbar → widget at the far left;
    /// left-aligned taskbar → widget just before the notification area.
    /// </summary>
    private static PixelRect EstimateWidget(PixelRect taskbar, IReadOnlyList<PixelRect> obstacles, double scale)
    {
        var width = (int)Math.Round(160 * scale);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        var leftAligned = key?.GetValue("TaskbarAl") is int al && al == 0;

        if (!leftAligned)
            return new PixelRect(taskbar.Left + (int)Math.Round(4 * scale), taskbar.Top, width, taskbar.Height);

        // The tray icons are the right-most cluster of buttons.
        var trayLeft = obstacles.Where(o => o.Left > taskbar.CenterX).Select(o => o.Left).DefaultIfEmpty(taskbar.Right - (int)(300 * scale)).Min();
        return new PixelRect(trayLeft - width - (int)Math.Round(8 * scale), taskbar.Top, width, taskbar.Height);
    }
}
