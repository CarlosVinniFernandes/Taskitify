using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>
/// What the taskbar overlay shows. Every element (cover, caption, controls, progress) is
/// optional and driven by <see cref="AppSettings"/>, from "lyrics only" to a full player.
/// </summary>
public sealed class TaskbarViewModel : ObservableObject, IDisposable
{
    private static readonly Color White = Colors.White;

    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly AppVolumeService _volume;
    private readonly DispatcherTimer _progressTimer;

    private string _mainText = string.Empty;
    private string _caption = string.Empty;
    private ImageSource? _cover;
    private bool _isCalibrating;
    private bool _isActive;
    private bool _wasCompact;
    private (Color, Color?) _lastColors;
    private Color _taskbarColor = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private Brush _taskbarBrush = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private Brush _topLine = Brushes.Transparent;
    private Brush _foreground = Brushes.White;
    private Brush _secondary = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
    private Brush _hover = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private double _progress;
    private string _volumeGlyph = "";
    private string _volumeTip = LocalizationService.Get("SpotifyVolume");

    public TaskbarViewModel(ISpotifyMonitor monitor, LyricsSyncService sync, SettingsService settings, AppVolumeService volume)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        _volume = volume;
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        PlayPauseCommand = new RelayCommand(() => _ = _monitor.PlayPauseAsync());
        NextCommand = new RelayCommand(() => _ = _monitor.NextAsync());
        PreviousCommand = new RelayCommand(() => _ = _monitor.PreviousAsync());
        ShuffleCommand = new RelayCommand(() => _ = _monitor.ToggleShuffleAsync());
        RepeatCommand = new RelayCommand(() => _ = _monitor.CycleRepeatAsync());
        MuteCommand = new RelayCommand(() =>
        {
            _volume.ToggleMute();
            RefreshVolume();
        });

        _monitor.TrackChanged += OnChanged;
        _monitor.ThumbnailChanged += OnThumbnailChanged;
        _monitor.PlaybackChanged += OnPlaybackChanged;
        _monitor.TimelineChanged += OnTimelineChanged;
        _sync.LyricsChanged += OnChanged;
        _sync.CurrentLineChanged += OnChanged;
        _settings.Changed += OnSettingsChanged;
        ApplyForeground();
        Refresh();
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand RepeatCommand { get; }
    public ICommand MuteCommand { get; }

    public string MainText { get => _mainText; private set => Set(ref _mainText, value); }

    public string Caption { get => _caption; private set => Set(ref _caption, value); }

    public ImageSource? Cover { get => _cover; private set => Set(ref _cover, value); }

    private AppSettings S => _settings.Current;

    /// <summary>
    /// Paused with "minimize" chosen: a compact player (cover, title, play button) the size of
    /// the weather widget, instead of hiding.
    /// </summary>
    public bool IsCompact =>
        S.PausedBehavior == PausedBehavior.Minimize
        && _monitor.Track is not null
        && _monitor.Status is PlaybackState.Paused or PlaybackState.Stopped;

    /// <summary>The layout in effect: the normal player, or the minimized one while paused.</summary>
    private BarLayout L => S.Layout(IsCompact);

    public bool ShowCover => L.ShowCover;

    public bool ShowMainText => L.ShowText;

    public bool ShowCaption => L.ShowCaption && !string.IsNullOrEmpty(Caption);

    public bool ShowTextArea => L.AnyText;

    public bool ShowControls => L.AnyControl;

    public bool ShowShuffle => L.ShowShuffle && _monitor.CanShuffle;

    public bool ShowPrevious => L.ShowPrevious;

    public bool ShowPlayPause => L.ShowPlayPause;

    public bool ShowNext => L.ShowNext;

    public bool ShowRepeat => L.ShowRepeat && _monitor.CanRepeat;

    public bool ShowVolume => L.ShowVolume;

    public bool ShowProgress => L.ShowProgress;

    private bool ProgressBesideCover => L.ShowProgress && S.ProgressPlacement == ProgressPlacement.BesideCover && L.ShowCover;

    /// <summary>The cover grows to the pill's full height when the progress line runs beside it.</summary>
    public double CoverSize => ProgressBesideCover ? 36 : 30;

    /// <summary>
    /// Thin line just under the 30 px controls. Beside the cover it starts after it:
    /// 8 (content margin) + 36 (cover) + 10 (gap).
    /// </summary>
    public Thickness ProgressMargin => ProgressBesideCover ? new Thickness(54, 0, 10, 5) : new Thickness(10, 0, 10, 5); // 1 px line at y 42, at the pill's bottom edge

    public bool IsGradient => S.Background == OverlayBackground.AlbumGradient;

    public double FontSize => S.FontSize;

    public double CaptionFontSize => Math.Max(9, S.FontSize - 1);

    public bool EnableAnimations => S.EnableAnimations;

    public bool Marquee => S.MarqueeLongText;

    public Color GradientStart => _monitor.Palette.Start;

    public Color GradientEnd => _monitor.Palette.End;

    public bool IsPlaying => _monitor.IsPlaying;

    public string PlayGlyph => IsPlaying ? "" : "";

    public bool IsShuffleActive => _monitor.IsShuffleActive;

    public bool IsRepeatActive => _monitor.RepeatMode != RepeatMode.None;

    public string RepeatGlyph => _monitor.RepeatMode == RepeatMode.Track ? "" : "";

    /// <summary>Spotify green when on, dimmed when off.</summary>
    public Brush ShuffleBrush => IsShuffleActive ? ActiveBrush : Secondary;

    public Brush RepeatBrush => IsRepeatActive ? ActiveBrush : Secondary;

    /// <summary>Glyph colour on the filled play circle.</summary>
    public Brush InverseForeground => ((SolidColorBrush)Foreground).Color == White ? DarkGlyph : WhiteBrush;

    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x1E, 0xD7, 0x60));
    private static readonly Brush DarkGlyph = Frozen(Color.FromRgb(0x12, 0x12, 0x12));
    private static readonly Brush WhiteBrush = Frozen(Colors.White);

    public string VolumeGlyph { get => _volumeGlyph; private set => Set(ref _volumeGlyph, value); }

    public string VolumeTip { get => _volumeTip; private set => Set(ref _volumeTip, value); }

    /// <summary>0..1 playback progress for the thin bottom line.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public bool IsCalibrating
    {
        get => _isCalibrating;
        set
        {
            if (Set(ref _isCalibrating, value))
                Refresh();
        }
    }

    public Brush TaskbarBrush { get => _taskbarBrush; private set => Set(ref _taskbarBrush, value); }

    public Brush TopLine { get => _topLine; private set => Set(ref _topLine, value); }

    public Brush Foreground { get => _foreground; private set => Set(ref _foreground, value); }

    public Brush Secondary { get => _secondary; private set => Set(ref _secondary, value); }

    public Brush Hover { get => _hover; private set => Set(ref _hover, value); }

    /// <summary>Overlay visible or not: the progress timer only runs while it is.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        UpdateProgressTimer();
        if (active)
        {
            UpdateProgress();
            if (ShowVolume)
                RefreshVolume();
        }
    }

    /// <summary>The taskbar colour sampled next to the overlay (fills the gaps around the gradient).</summary>
    public void ApplyTaskbarColors(Color background, Color? topLine)
    {
        // Sampled every few seconds; only touch the visuals when the colour really changed.
        if ((background, topLine) == _lastColors)
            return;
        _lastColors = (background, topLine);
        _taskbarColor = background;
        TaskbarBrush = Frozen(background);
        TopLine = Frozen(topLine ?? background);
        ApplyForeground();
    }

    /// <summary>Mouse wheel over the volume button: ±5 %.</summary>
    public void NudgeVolume(int direction)
    {
        if (_volume.Get() is { } v)
        {
            _volume.SetVolume(v.Volume + (0.05f * direction));
            RefreshVolume();
        }
    }

    public void RefreshVolume()
    {
        var v = _volume.Get();
        if (v is not { } state)
        {
            VolumeGlyph = "";
            VolumeTip = LocalizationService.Get("VolumeUnavailable");
            return;
        }
        VolumeGlyph = state.Muted || state.Volume <= 0.001f ? ""
            : state.Volume < 0.34f ? ""
            : state.Volume < 0.67f ? ""
            : "";
        VolumeTip = state.Muted
            ? LocalizationService.Get("SpotifyMuted")
            : LocalizationService.Format("VolumeDetail", Math.Round(state.Volume * 100));
    }

    public void Dispose()
    {
        _progressTimer.Stop();
        _monitor.TrackChanged -= OnChanged;
        _monitor.ThumbnailChanged -= OnThumbnailChanged;
        _monitor.PlaybackChanged -= OnPlaybackChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _sync.LyricsChanged -= OnChanged;
        _sync.CurrentLineChanged -= OnChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void OnThumbnailChanged(object? sender, EventArgs e)
    {
        Cover = _monitor.Thumbnail;
        OnPropertyChanged(nameof(GradientStart));
        OnPropertyChanged(nameof(GradientEnd));
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        var compact = IsCompact;
        if (compact != _wasCompact)
        {
            // Layout flags all depend on the compact state.
            _wasCompact = compact;
            OnPropertyChanged(string.Empty);
            Refresh();
        }
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(IsShuffleActive));
        OnPropertyChanged(nameof(IsRepeatActive));
        OnPropertyChanged(nameof(ShuffleBrush));
        OnPropertyChanged(nameof(RepeatBrush));
        OnPropertyChanged(nameof(RepeatGlyph));
        OnPropertyChanged(nameof(ShowShuffle));
        OnPropertyChanged(nameof(ShowRepeat));
        UpdateProgress();
        UpdateProgressTimer();
    }

    private void OnTimelineChanged(object? sender, EventArgs e) => UpdateProgress();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _wasCompact = IsCompact;
        RefreshVolume();
        OnPropertyChanged(string.Empty); // layout flags, fonts, colours
        ApplyForeground();
        UpdateProgressTimer();
        Refresh();
    }

    private void UpdateProgressTimer()
    {
        var run = _isActive && ShowProgress && _monitor.IsPlaying;
        if (run != _progressTimer.IsEnabled)
            _progressTimer.IsEnabled = run;
    }

    private void UpdateProgress()
    {
        var d = _monitor.Duration;
        Progress = d is { } duration && duration > TimeSpan.Zero
            ? Math.Clamp(_monitor.Position.TotalSeconds / duration.TotalSeconds, 0, 1)
            : 0;
    }

    /// <summary>On the album gradient text is always white; on the taskbar it follows luminance.</summary>
    private void ApplyForeground()
    {
        var dark = IsGradient
            || ((0.299 * _taskbarColor.R) + (0.587 * _taskbarColor.G) + (0.114 * _taskbarColor.B)) / 255.0 < 0.5;
        Foreground = Frozen(dark ? White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        Secondary = Frozen(dark ? Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA0, 0, 0, 0));
        Hover = Frozen(dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0));
        OnPropertyChanged(nameof(InverseForeground));
        OnPropertyChanged(nameof(ShuffleBrush));
        OnPropertyChanged(nameof(RepeatBrush));
    }

    private void Refresh()
    {
        Cover = _monitor.Thumbnail;
        var track = _monitor.Track;

        if (track is null)
        {
            MainText = IsCalibrating ? LocalizationService.Get("CalibrationTitle") : string.Empty;
            Caption = IsCalibrating ? LocalizationService.Get("DragHint") : string.Empty;
            OnPropertyChanged(nameof(ShowCaption));
            return;
        }

        var trackLine = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Title} · {track.Artist}";

        if (IsCompact || S.PrimaryText == PrimaryText.Track)
        {
            // Media-widget style: title on top, artist below.
            MainText = track.Title;
            Caption = track.Artist;
        }
        else if (_sync.State == LyricsState.Synced && _sync.CurrentLine is { } line)
        {
            // Empty LRC lines are instrumental gaps.
            MainText = string.IsNullOrWhiteSpace(line.Text) ? "♪ ♪ ♪" : line.Text;
            Caption = trackLine;
        }
        else
        {
            // Before the first line, or no synced lyrics: show the song, never invented lines.
            MainText = track.Title;
            Caption = track.Artist;
        }
        OnPropertyChanged(nameof(ShowCaption));
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
