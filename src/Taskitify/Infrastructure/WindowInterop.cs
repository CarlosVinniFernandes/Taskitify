using System.Windows;
using System.Windows.Interop;
using Taskitify.Core.Layout;
using static Taskitify.Infrastructure.NativeMethods;

namespace Taskitify.Infrastructure;

public sealed record MonitorDetails(IntPtr Handle, string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

internal static class WindowInterop
{
    public static IntPtr EnsureHandle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>Hide from Alt+Tab and the taskbar; optionally never take activation (focus).</summary>
    public static void MakeToolWindow(IntPtr hwnd, bool noActivate)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        if (noActivate)
            ex |= WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    public static void SetBounds(IntPtr hwnd, PixelRect r, bool topmost, bool show = false)
    {
        var flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | (show ? SWP_SHOWWINDOW : 0);
        if (!topmost)
            flags |= SWP_NOZORDER;
        SetWindowPos(hwnd, HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, flags);
    }

    /// <summary>Windows 11 may round top-level window corners; no-op on Windows 10.</summary>
    public static void DisableRoundedCorners(IntPtr hwnd)
    {
        var doNotRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));
    }

    /// <summary>
    /// Keep the window visible during Aero Peek. Hovering a taskbar thumbnail makes DWM fade
    /// out every window except the previewed one and the taskbar itself; the overlay is part
    /// of the taskbar visually, so it must stay (otherwise the weather widget shows through).
    /// </summary>
    public static void ExcludeFromPeek(IntPtr hwnd)
    {
        var exclude = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_EXCLUDED_FROM_PEEK, ref exclude, sizeof(int));
    }

    /// <summary>Dark caption bar to match a dark app theme (Windows 10 20H1+ / 11).</summary>
    public static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = EnsureHandle(window);
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public static void SetPosition(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public static void BringToTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public static PixelRect GetBounds(IntPtr hwnd) =>
        GetWindowRect(hwnd, out var r) ? PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : default;

    public static double GetScale(IntPtr hwnd)
    {
        var dpi = hwnd == IntPtr.Zero ? 96u : GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var len = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    public static MonitorDetails? GetMonitor(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        if (hMonitor == IntPtr.Zero || !GetMonitorInfo(hMonitor, ref info))
            return null;
        return new MonitorDetails(
            hMonitor,
            info.szDevice,
            PixelRect.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            PixelRect.FromLTRB(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            (info.dwFlags & 1) != 0);
    }

    public static MonitorDetails? MonitorForWindow(IntPtr hwnd) => GetMonitor(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    public static MonitorDetails? MonitorForRect(PixelRect r)
    {
        var rect = new RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return GetMonitor(MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST));
    }

    public static bool IsTaskbarAutoHide()
    {
        var data = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        var state = (int)SHAppBarMessage(ABM_GETSTATE, ref data).ToUInt32();
        return (state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>
    /// True when a fullscreen app (game, video, presentation) owns the foreground, the case in
    /// which Windows itself hides the taskbar.
    /// </summary>
    /// <param name="monitorBounds">Monitor whose taskbar we cover; fullscreen elsewhere does not count.</param>
    /// <param name="shellSaysFullscreen">Last ABN_FULLSCREENAPP state sent by the shell.</param>
    public static bool IsFullscreenOn(PixelRect monitorBounds, bool shellSaysFullscreen)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;
        var cls = GetClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;
        GetWindowThreadProcessId(fg, out var pid);
        if (pid == (uint)Environment.ProcessId)
            return false; // our own popup / settings

        var monitor = MonitorForWindow(fg);
        if (monitor is null || monitor.Bounds != monitorBounds)
            return false;

        // The shell's own verdict (the same one that hides the taskbar).
        if (shellSaysFullscreen)
            return true;

        if (SHQueryUserNotificationState(out var state) == 0
            && state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
            return true;

        // Borderless windows covering the whole monitor (browser F11, video players, games).
        // Windows with a caption (maximized apps, including custom-chrome ones) never count.
        if ((GetWindowLong(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION)
            return false;
        return GetBounds(fg).Contains(monitor.Bounds);
    }

    /// <summary>
    /// Registers a window as an app bar that reserves no space, only so the shell sends it
    /// ABN_FULLSCREENAPP when a fullscreen app opens or closes.
    /// </summary>
    public static bool RegisterAppBar(IntPtr hwnd, uint callbackMessage)
    {
        var data = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = callbackMessage };
        return SHAppBarMessage(ABM_NEW, ref data) != UIntPtr.Zero;
    }

    public static void UnregisterAppBar(IntPtr hwnd)
    {
        var data = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
    }

    /// <summary>Average colour of a few screen pixels (physical coordinates). Null if none are readable.</summary>
    public static System.Windows.Media.Color? SampleScreen(IEnumerable<(int X, int Y)> points)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return null;
        try
        {
            int r = 0, g = 0, b = 0, n = 0;
            foreach (var (x, y) in points)
            {
                var c = GetPixel(hdc, x, y);
                if (c == CLR_INVALID)
                    continue;
                r += (int)(c & 0xFF);
                g += (int)((c >> 8) & 0xFF);
                b += (int)((c >> 16) & 0xFF);
                n++;
            }
            return n == 0 ? null : System.Windows.Media.Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}

internal static class FocusHelper
{
    /// <summary>
    /// Bring a window to the foreground after an explicit user click on our UI. Plain
    /// SetForegroundWindow can be refused by the foreground lock; briefly sharing input state
    /// with the current foreground thread is the documented-behaviour workaround.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd)
            return;
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var thisThread = GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == thisThread)
        {
            SetForegroundWindow(hwnd);
            return;
        }
        AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            AttachThreadInput(thisThread, foregroundThread, false);
        }
    }
}

/// <summary>
/// Low-level mouse hook used only while the mini player is open, to close it on a click
/// outside even if it could not take activation. Runs in this process; nothing is injected.
/// </summary>
internal sealed class OutsideClickWatcher : IDisposable
{
    private readonly LowLevelMouseProc _proc; // kept alive for the native side
    private readonly Action<int, int> _onButtonDown;
    private IntPtr _hook;

    public OutsideClickWatcher(Action<int, int> onButtonDown)
    {
        _onButtonDown = onButtonDown;
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook == IntPtr.Zero)
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // Never block inside a low-level hook: hand off and return immediately.
            _onButtonDown(data.X, data.Y);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

/// <summary>Out-of-context WinEvent hook for foreground changes. No code is injected anywhere.</summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventDelegate _callback; // kept alive for the native side
    private readonly Action<IntPtr> _onChanged;
    private IntPtr _hook;

    public ForegroundWatcher(Action<IntPtr> onChanged)
    {
        _onChanged = onChanged;
        _callback = (_, _, hwnd, _, _, _, _) => _onChanged(hwnd);
        // Must be created on a thread with a message loop (the UI thread); callbacks arrive there.
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
