using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Core.Media;

namespace Taskitify.Linux;

/// <summary>Cover, controls, progress and the full lyrics scrolling with the current line highlighted.</summary>
internal sealed class MiniPlayerWindow : Window
{
    private readonly AppHost _host;
    private readonly Border _background;
    private readonly ContentControl _coverHolder = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Ui.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 13, Foreground = Ui.SoftWhite, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Slider _progress = new() { Minimum = 0, Maximum = 1, MinWidth = 120 };
    private readonly TextBlock _elapsed = new() { FontSize = 11, Foreground = Ui.SoftWhite };
    private readonly TextBlock _total = new() { FontSize = 11, Foreground = Ui.SoftWhite };
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _lines = new() { Spacing = 6 };
    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<TextBlock> _lineBlocks = [];
    private object? _renderedLyrics;
    private int _highlighted = -1;
    private bool _scrubbing;
    private bool _free;

    public MiniPlayerWindow(AppHost host)
    {
        _host = host;
        Width = 380;
        Height = 620;
        MinWidth = 320;
        MinHeight = 420;
        Title = host.T("MiniPlayerTitle").Replace('?', '—');
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = _lines,
        };
        // Scrolling by hand pauses the auto-follow until the next line change.
        _scroller.PointerWheelChanged += (_, _) => _free = true;

        _progress.AddHandler(PointerPressedEvent, (_, _) => _scrubbing = true, handledEventsToo: true);
        _progress.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _scrubbing = false;
            _ = _host.Media.SeekAsync(TimeSpan.FromSeconds(_progress.Value));
        }, handledEventsToo: true);

        var timeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        timeRow.Children.Add(_elapsed);
        timeRow.Children.Add(_progress);
        Grid.SetColumn(_progress, 1);
        timeRow.Children.Add(_total);
        Grid.SetColumn(_total, 2);

        var top = new StackPanel { Spacing = 4, Margin = new Thickness(20, 20, 20, 8) };
        top.Children.Add(_coverHolder);
        top.Children.Add(new Border { Height = 8 });
        top.Children.Add(_title);
        top.Children.Add(_artist);
        top.Children.Add(timeRow);
        top.Children.Add(_controls);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new Border { Padding = new Thickness(24, 8, 24, 24), Child = _scroller });

        _background = new Border { Child = root };
        Content = _background;

        _host.Changed += Refresh;
        _tick.Tick += (_, _) => Tick();
        Opened += (_, _) => { _tick.Start(); Refresh(); };
        Closing += (_, e) =>
        {
            // Closing only hides: the app lives in the tray.
            e.Cancel = true;
            Hide();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Hide();
        };
        Refresh();
    }

    private void Refresh()
    {
        if (!IsVisible)
            return;
        var media = _host.Media;
        var track = media.Track;
        _background.Background = Ui.Gradient(_host.Cover, diagonal: true);
        _coverHolder.Content = Ui.CoverView(_host.Cover, 200, 12);
        _title.Text = track?.Title ?? _host.T("NothingPlaying");
        _artist.Text = track?.Artist ?? "";
        _progress.Maximum = Math.Max(1, track?.Duration?.TotalSeconds ?? 1);
        _progress.IsEnabled = media.CanSeek && track?.Duration is not null;
        _total.Text = track?.Duration is { } d ? Ui.Format(d) : "";

        _controls.Children.Clear();
        _controls.Children.Add(Ui.IconButton(Icons.Shuffle, _host.T("Shuffle"), () => _ = media.ToggleShuffleAsync(), 20, stroke: true));
        _controls.Children[^1].Opacity = media.IsShuffleActive ? 1 : 0.45;
        _controls.Children.Add(Ui.IconButton(Icons.Previous, _host.T("Previous"), () => _ = media.PreviousAsync(), 20));
        _controls.Children.Add(Ui.IconButton(Ui.PlayPathFor(media.Status), _host.T("PlayPause"), () => _ = media.PlayPauseAsync(), 28));
        _controls.Children.Add(Ui.IconButton(Icons.Next, _host.T("Next"), () => _ = media.NextAsync(), 20));
        _controls.Children.Add(Ui.IconButton(Icons.Repeat, _host.T("Repeat"), () => _ = media.CycleRepeatAsync(), 20, stroke: true));
        _controls.Children[^1].Opacity = media.Repeat == MediaRepeatMode.None ? 0.45 : 1;

        RenderLyrics();
        Highlight(_host.Session.CurrentIndex);
        Tick();
    }

    private void Tick()
    {
        if (!IsVisible || _scrubbing)
            return;
        var pos = _host.Media.Position;
        _progress.Value = Math.Min(_progress.Maximum, pos.TotalSeconds);
        _elapsed.Text = Ui.Format(pos);
    }

    private void RenderLyrics()
    {
        var session = _host.Session;
        // Rebuild only when the lyrics themselves changed, not on every position update.
        object? key = session.State == LyricsSessionState.Synced ? session.Timeline : (object)session.State;
        if (ReferenceEquals(key, _renderedLyrics) || Equals(key, _renderedLyrics))
            return;
        _renderedLyrics = key;
        _lines.Children.Clear();
        _lineBlocks.Clear();
        _highlighted = -1;
        _free = false;

        switch (session.State)
        {
            case LyricsSessionState.Synced when session.Timeline is { } timeline:
                for (var i = 0; i < timeline.Lines.Count; i++)
                {
                    var index = i;
                    var line = timeline.Lines[i];
                    var block = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ui.DimWhite,
                        TextWrapping = TextWrapping.Wrap,
                        Cursor = new Cursor(StandardCursorType.Hand),
                    };
                    block.PointerPressed += (_, _) =>
                        _ = _host.Media.SeekAsync(timeline.Lines[index].Time - TimeSpan.FromMilliseconds(_host.Settings.SyncOffsetMs));
                    _lineBlocks.Add(block);
                    _lines.Children.Add(block);
                }
                break;
            case LyricsSessionState.PlainOnly:
                _lines.Children.Add(Message(session.PlainText ?? "", wrap: true));
                break;
            case LyricsSessionState.Loading:
                _lines.Children.Add(Message(_host.T("LoadingLyrics")));
                break;
            case LyricsSessionState.Instrumental:
                _lines.Children.Add(Message(_host.T("Instrumental")));
                break;
            case LyricsSessionState.Error:
                _lines.Children.Add(Message(session.ErrorMessage ?? _host.T("LyricsError")));
                break;
            case LyricsSessionState.NotFound:
                _lines.Children.Add(Message(_host.T("UnavailableLyrics")));
                break;
        }
    }

    private static TextBlock Message(string text, bool wrap = true) => new()
    {
        Text = text,
        FontSize = 15,
        Foreground = Ui.SoftWhite,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    private void Highlight(int index)
    {
        if (index == _highlighted && !_free)
            return;
        if (index != _highlighted)
            _free = false;
        if (_highlighted >= 0 && _highlighted < _lineBlocks.Count)
            Style(_lineBlocks[_highlighted], current: false);
        _highlighted = index;
        if (index < 0 || index >= _lineBlocks.Count)
            return;
        var block = _lineBlocks[index];
        Style(block, current: true);
        if (!_free)
            Dispatcher.UIThread.Post(() => Center(block), DispatcherPriority.Background);
    }

    /// <summary>Keep the current line in the middle of the lyrics area, like the Windows mini player.</summary>
    private void Center(TextBlock block)
    {
        var y = block.TranslatePoint(new Point(0, 0), _lines)?.Y ?? 0;
        var target = y + (block.Bounds.Height / 2) - (_scroller.Viewport.Height / 2);
        var max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _scroller.Offset = new Vector(0, Math.Clamp(target, 0, max));
    }

    private static void Style(TextBlock block, bool current)
    {
        block.Foreground = current ? Ui.White : Ui.DimWhite;
        block.FontSize = current ? 22 : 18;
    }
}
