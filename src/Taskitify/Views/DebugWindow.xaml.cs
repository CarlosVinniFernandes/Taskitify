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
        sb.AppendLine($"Sessões de mídia : {string.Join(" | ", _monitor.AllSessionAppIds.DefaultIfEmpty("(nenhuma)"))}");
        sb.AppendLine($"Sessão Spotify   : {_monitor.SessionAppId ?? "(não encontrada)"}");
        sb.AppendLine($"Estado           : {_monitor.Status}");
        sb.AppendLine($"Música           : {_monitor.Track?.Title ?? "-"}");
        sb.AppendLine($"Artista / Álbum  : {_monitor.Track?.Artist ?? "-"} / {_monitor.Track?.Album ?? "-"}");
        sb.AppendLine($"Posição          : {_monitor.Position:mm\\:ss\\.fff} / {(_monitor.Duration is { } d ? d.ToString(@"mm\:ss\.fff") : "?")}");
        sb.AppendLine($"Controles        : seek={_monitor.CanSeek} next={_monitor.CanGoNext} prev={_monitor.CanGoPrevious} shuffle={_monitor.CanShuffle}:{_monitor.IsShuffleActive} repeat={_monitor.CanRepeat}:{_monitor.RepeatMode}");
        sb.AppendLine($"Letra            : {_sync.State} ({_sync.Timeline?.Count ?? 0} linhas) linha {_sync.CurrentIndex}");
        sb.AppendLine($"Linha atual      : {_sync.CurrentLine?.Text ?? "-"}");
        var p = _overlay.Placement;
        sb.AppendLine($"Barra            : {(p is null ? "?" : $"{p.Taskbar} em {p.MonitorName}, escala {p.Scale:0.##}, auto-ocultar={p.AutoHide}")}");
        sb.Append($"Widget / overlay : {(p is null ? "?" : $"{p.Widget} (detectado={p.WidgetDetected})")} / {_overlay.Bounds} visível={_overlay.IsShown}");
        StateText.Text = sb.ToString();
    }
}
