using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Optional notification-area icon: Shell_NotifyIcon on a hidden window, with a WPF menu like the
/// bar's. (Not WinForms' NotifyIcon: WinForms would be ~22 MB of the package for this alone.)
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8000 + 0x71; // WM_APP + n
    private const uint IconId = 1;

    private readonly HwndSource _window;
    private readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _showSettings;
    private readonly ContextMenu _menu;
    private readonly MenuItem _debugItem;
    private readonly OutsideClickWatcher _outside;
    private readonly DispatcherTimer _menuEsc = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IntPtr _hIcon;
    private bool _visible;
    private Action? _balloonClick;

    public TrayIconService(Action showSettings, Action showMiniPlayer, Action showLyricCard, Action showDebug, Action exit, Action? showTour = null)
    {
        _showSettings = showSettings;

        var playerItem = Item(showMiniPlayer);
        // Minimal Mode, and the way back when its lyrics let clicks through (they cannot be clicked then).
        var minimalItem = Item(() => ToggleMinimal?.Invoke());
        var clicksItem = Item(() => ToggleMinimalClicks?.Invoke());
        var cardItem = Item(showLyricCard);
        var settingsItem = Item(showSettings);
        var tourItem = showTour is null ? null : Item(showTour);
        _debugItem = Item(showDebug);
        var exitItem = Item(exit);

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(playerItem);
        _menu.Items.Add(minimalItem);
        _menu.Items.Add(clicksItem);
        _menu.Items.Add(cardItem);
        _menu.Items.Add(settingsItem);
        if (tourItem is not null)
            _menu.Items.Add(tourItem);
        _menu.Items.Add(_debugItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(exitItem);

        _menu.Opened += (_, _) =>
        {
            playerItem.Header = LocalizationService.Get("ShowPlayer");
            var (minimal, clicksThrough) = Minimal?.Invoke() ?? (false, false);
            minimalItem.Header = LocalizationService.Get("NavMinimal");
            minimalItem.IsChecked = minimal;
            clicksItem.Header = LocalizationService.Get("MinimalClickThrough");
            clicksItem.IsChecked = clicksThrough;
            clicksItem.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;
            cardItem.Header = LocalizationService.Get("LyricCardOpen");
            cardItem.IsEnabled = CanMakeLyricCard?.Invoke() ?? true;
            settingsItem.Header = LocalizationService.Get("Settings");
            if (tourItem is not null)
                tourItem.Header = LocalizationService.Get("TourOpen");
            _debugItem.Header = LocalizationService.Get("Diagnostics");
            exitItem.Header = LocalizationService.Get("Exit");
            _outside!.Start();
            WindowInterop.RaisePopup(_menu);
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE); // clear a stale "pressed since" bit
            _menuEsc.Start();
        };
        _menu.Closed += (_, _) =>
        {
            _outside!.Stop();
            _menuEsc.Stop();
        };
        // As in the bar's menu: the hidden owner window has no keyboard focus to give the menu, so
        // an outside click (low-level hook, only while open) and Esc (polled) close it.
        _outside = new OutsideClickWatcher((x, y) =>
        {
            if (!WindowInterop.PopupContains(_menu, x, y))
                _menu.Dispatcher.BeginInvoke(() => _menu.IsOpen = false);
        });
        _menuEsc.Tick += (_, _) =>
        {
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0)
                _menu.IsOpen = false;
        };

        // A hidden, never-shown top-level window: it receives the icon's messages and can be
        // brought to the foreground so the menu behaves like the shell expects.
        _window = new HwndSource(new HwndSourceParameters("Taskitify tray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, not visible
            ExtendedWindowStyle = 0x00000080,          // WS_EX_TOOLWINDOW
        });
        _window.AddHook(WndProc);
        _hIcon = LoadLogo();
    }

    private static MenuItem Item(Action click)
    {
        var item = new MenuItem();
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Minimal Mode's state: on, and letting clicks through.</summary>
    public Func<(bool On, bool ClickThrough)>? Minimal { get; set; }

    public Action? ToggleMinimal { get; set; }

    public Action? ToggleMinimalClicks { get; set; }

    /// <summary>Whether a song is playing, so the lyric card item can be used.</summary>
    public Func<bool>? CanMakeLyricCard { get; set; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (value == _visible)
                return;
            _visible = value;
            if (value)
                Add();
            else
            {
                _menu.IsOpen = false;
                var data = NewData(0);
                Shell_NotifyIcon(NIM_DELETE, ref data);
            }
        }
    }

    public bool DebugEnabled
    {
        set => _debugItem.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <param name="onClick">Runs if the user clicks this notification (not a later one).</param>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        if (!_visible)
            return;
        _balloonClick = onClick;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.uTimeoutOrVersion = (uint)(onClick is null ? 4000 : 10000);
        data.dwInfoFlags = NIIF_NONE;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        Visible = false;
        _outside.Dispose();
        _menuEsc.Stop();
        _window.RemoveHook(WndProc);
        _window.Dispose();
        if (_hIcon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_hIcon);
        _hIcon = IntPtr.Zero;
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_ADD, ref data);
        // Version 4: the callback carries the event in lParam's low word and the anchor point in wParam.
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon,
        szTip = "Taskitify",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated && _visible)
        {
            Add(); // Explorer restarted: the icon is gone until added again (NotifyIcon did this too).
            return IntPtr.Zero;
        }
        if (msg != CallbackMessage)
            return IntPtr.Zero;

        handled = true;
        switch ((int)(lParam.ToInt64() & 0xFFFF))
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                _showSettings();
                break;
            case WM_CONTEXTMENU:
                NativeMethods.SetForegroundWindow(hwnd);
                _menu.IsOpen = true;
                break;
            case NIN_BALLOONUSERCLICK:
                var click = _balloonClick;
                _balloonClick = null;
                click?.Invoke();
                break;
            case NIN_BALLOONTIMEOUT:
            case NIN_BALLOONHIDE:
                _balloonClick = null;
                break;
        }
        return IntPtr.Zero;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>The app logo at the notification area's icon size (its own frame for 100-200 %).</summary>
    private static IntPtr LoadLogo()
    {
        using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream("Taskitify.ico")
            ?? throw new InvalidOperationException("Embedded icon Taskitify.ico is missing.");
        var ico = new byte[stream.Length];
        stream.ReadExactly(ico);
        var size = GetSystemMetrics(SM_CXSMICON);

        // ICONDIR: reserved, type, count; then 16-byte entries (width, height, ..., bytes, offset).
        int count = BitConverter.ToUInt16(ico, 4), best = -1, bestSize = 0;
        for (var i = 0; i < count; i++)
        {
            var w = ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i];
            // The smallest frame at least as large as asked for, else the largest there is.
            if (best < 0 || (w >= size && (bestSize < size || w < bestSize)) || (bestSize < size && w > bestSize))
            {
                best = i;
                bestSize = w;
            }
        }
        var entry = 6 + 16 * best;
        var length = BitConverter.ToInt32(ico, entry + 8);
        var offset = BitConverter.ToInt32(ico, entry + 12);
        var handle = CreateIconFromResourceEx(ref ico[offset], (uint)length, true, 0x00030000, size, size, 0);
        return handle != IntPtr.Zero ? handle : throw new InvalidOperationException("Could not create the tray icon.");
    }

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_NONE = 0, NOTIFYICON_VERSION_4 = 4;
    private const int WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    private const int NIN_BALLOONHIDE = 0x0403, NIN_BALLOONTIMEOUT = 0x0404, NIN_BALLOONUSERCLICK = 0x0405;
    private const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(ref byte data, uint size, bool icon, uint version, int cx, int cy, uint flags);
}
