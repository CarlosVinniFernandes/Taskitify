using System.Text;
using System.Windows;
using System.Windows.Threading;
using Taskitify.Infrastructure;
using Taskitify.Services;

namespace Taskitify.Views;

/// <summary>Live view of what the services see: media sessions, track, position, lyrics, placement.</summary>
public partial class DebugWindow : Window
{
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly TaskbarOverlayService _overlay;
    private readonly DispatcherTimer _timer;

    public DebugWindow(ISpotifyMonitor monitor, LyricsSyncService sync, TaskbarOverlayService overlay)
    {
        InitializeComponent();
        _monitor = monitor;
        _sync = sync;
        _overlay = overlay;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Render();
        Loaded += (_, _) =>
        {
            LogText.Text = string.Join(Environment.NewLine, Log.Snapshot());
            LogText.ScrollToEnd();
            Log.LineAdded += OnLogLine;
            _timer.Start();
            Render();
        };
        Closed += (_, _) =>
        {
            Log.LineAdded -= OnLogLine;
            _timer.Stop();
        };
    }

    private void OnLogLine(string line) => Dispatcher.InvokeAsync(() =>
    {
        LogText.AppendText(Environment.NewLine + line);
        LogText.ScrollToEnd();
    });

    private void Render()
    {
        var sb = new StringBuilder();
        static string T(string key) => LocalizationService.Get(key);
        static string B(bool value) => T(value ? "Yes" : "No");
        void Line(string key, object value) => sb.AppendLine($"{T(key)}: {value}");
        Line("MediaSessions", string.Join(" | ", _monitor.AllSessionAppIds.DefaultIfEmpty(T("None"))));
        Line("SpotifySession", _monitor.SessionAppId ?? T("NotFound"));
        Line("State", T(_monitor.Status.ToString()));
        Line("Track", _monitor.Track?.Title ?? "-");
        Line("ArtistAlbum", $"{_monitor.Track?.Artist ?? "-"} / {_monitor.Track?.Album ?? "-"}");
        Line("Position", $"{_monitor.Position:mm\\:ss\\.fff} / {(_monitor.Duration is { } d ? d.ToString(@"mm\:ss\.fff") : "?")}");
        Line("Controls", $"{T("Seek")}={B(_monitor.CanSeek)} {T("Next")}={B(_monitor.CanGoNext)} {T("Previous")}={B(_monitor.CanGoPrevious)} {T("Shuffle")}={B(_monitor.CanShuffle)}:{B(_monitor.IsShuffleActive)} {T("Repeat")}={B(_monitor.CanRepeat)}:{T(_monitor.RepeatMode.ToString())}");
        Line("Lyrics", LocalizationService.Format("LyricsDiagnostic", T(_sync.State.ToString()), _sync.Timeline?.Count ?? 0, _sync.CurrentIndex));
        Line("CurrentLine", _sync.CurrentLine?.Text ?? "-");
        var p = _overlay.Placement;
        Line("Taskbar", p is null ? "?" : LocalizationService.Format("PlacementDiagnostic", p.Taskbar, p.MonitorName, p.Scale, B(p.AutoHide)));
        sb.Append(LocalizationService.Format("OverlayDiagnostic", p?.Widget.ToString() ?? "?", p is null ? "?" : B(p.WidgetDetected), _overlay.Bounds, B(_overlay.IsShown)));
        StateText.Text = sb.ToString();
    }
}
