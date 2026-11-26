using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

public sealed class LyricLineViewModel : ObservableObject
{
    private bool _isCurrent;
    private bool _isPast;

    public LyricLineViewModel(int index, TimeSpan time, string text)
    {
        Index = index;
        Time = time;
        Text = string.IsNullOrWhiteSpace(text) ? "♪" : text;
    }

    public int Index { get; }

    public TimeSpan Time { get; }

    public string Text { get; }

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    public bool IsPast { get => _isPast; set => Set(ref _isPast, value); }

    private double _emphasis = 1;
    private double _scale = 1;
    private System.Windows.FontWeight _weight = System.Windows.FontWeights.Normal;

    /// <summary>The line's opacity under the chosen highlight style.</summary>
    public double Emphasis { get => _emphasis; set => Set(ref _emphasis, value); }

    /// <summary>Drawn larger (render transform: the layout does not move).</summary>
    public double Scale { get => _scale; set => Set(ref _scale, value); }

    public System.Windows.FontWeight Weight { get => _weight; set => Set(ref _weight, value); }

    /// <summary>The emphasis of a line <paramref name="distance"/> lines from the current one (0 = current, negative = past).</summary>
    public void Emphasize(LyricsHighlight highlight, int distance, bool started)
    {
        var current = started && distance == 0;
        var away = Math.Abs(distance);
        switch (highlight)
        {
            case LyricsHighlight.Expressive:
                Emphasis = current ? 1 : distance < 0 ? 0.32 : 0.42;
                Scale = current ? 1.12 : 1;
                Weight = current ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
                break;
            case LyricsHighlight.Progressive:
                Emphasis = !started ? 0.5 : away switch { 0 => 1, 1 => 0.62, 2 => 0.42, 3 => 0.3, _ => 0.22 };
                Scale = current ? 1.05 : 1;
                Weight = current ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
                break;
            default:
                Emphasis = current ? 1 : distance < 0 ? 0.36 : 0.48;
                Scale = 1;
                Weight = current ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
                break;
        }
    }
}

/// <summary>
/// State of the popup. It only listens to services and runs its 4 Hz progress timer while
/// <see cref="IsActive"/> is true (popup visible), so a hidden popup costs nothing.
/// </summary>
public sealed class MiniPlayerViewModel : ObservableObject, IDisposable
{
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _progressTimer;

    private bool _isActive;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private ImageSource? _cover;
    private bool _isPlaying;
    private double _positionSeconds;
    private double _durationSeconds = 1;
    private string _positionText = "0:00";
    private string _durationText = "0:00";
    private string? _statusMessage;
    private string? _credit;
    private int _currentIndex = -1;
    private double? _previewSeconds;

    public MiniPlayerViewModel(ISpotifyMonitor monitor, LyricsSyncService sync, SettingsService settings, AppVolumeService volume, SpotifyLikeService likes)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        Volume = new VolumeControl(volume);
        Like = new LikeControl(likes);
        Like.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowLike));
            OnPropertyChanged(nameof(ShowControls));
        };
        ShareCommand = new RelayCommand(() => ShareRequested?.Invoke(this, EventArgs.Empty), () => _monitor.Track is not null);
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        PlayPauseCommand = new RelayCommand(() => _ = _monitor.PlayPauseAsync(), () => _monitor.HasSession);
        NextCommand = new RelayCommand(() => _ = _monitor.NextAsync(), () => _monitor.CanGoNext);
        PreviousCommand = new RelayCommand(() => _ = _monitor.PreviousAsync(), () => _monitor.CanGoPrevious);
        ShuffleCommand = new RelayCommand(() => _ = _monitor.ToggleShuffleAsync());
        RepeatCommand = new RelayCommand(() => _ = _monitor.CycleRepeatAsync());
        SeekToLineCommand = new RelayCommand(p =>
        {
            if (p is LyricLineViewModel line && _monitor.CanSeek)
                _ = _monitor.SeekAsync(line.Time);
        });
    }

    public ObservableCollection<LyricLineViewModel> Lines { get; } = [];

    public ICommand PlayPauseCommand { get; }

    public ICommand NextCommand { get; }

    public ICommand PreviousCommand { get; }

    public ICommand SeekToLineCommand { get; }

    public ICommand ShuffleCommand { get; }

    public ICommand RepeatCommand { get; }

    public VolumeControl Volume { get; }

    /// <summary>Opens the lyric card maker for the song.</summary>
    public ICommand ShareCommand { get; }

    public event EventHandler? ShareRequested;

    public LikeControl Like { get; }

    // What the popup shows, chosen in Settings like the taskbar players (the lyrics always).
    private BarLayout L => _settings.Current.MiniPlayer;

    public bool ShowCover => L.ShowCover;

    public bool ShowTitle => L.ShowText;

    public bool ShowArtist => L.ShowCaption;

    public bool ShowHeader => L.ShowCover || L.ShowText || L.ShowCaption;

    public bool ShowShuffle => L.ShowShuffle && _monitor.CanShuffle;

    public bool ShowPrevious => L.ShowPrevious;

    public bool ShowPlayPause => L.ShowPlayPause;

    public bool ShowNext => L.ShowNext;

    public bool ShowRepeat => L.ShowRepeat && _monitor.CanRepeat;

    public bool ShowVolume => L.ShowVolume;

    /// <summary>The heart: chosen in Settings, and Spotify connected.</summary>
    public bool ShowLike => L.ShowLike && Like.IsAvailable;

    public bool ShowControls => ShowShuffle || L.ShowPrevious || L.ShowPlayPause || L.ShowNext || ShowRepeat || L.ShowVolume || ShowLike;

    /// <summary>The transport row at the bottom (the heart lives with the song, up top).</summary>
    public bool ShowTransport => ShowShuffle || L.ShowPrevious || L.ShowPlayPause || L.ShowNext || ShowRepeat || L.ShowVolume;

    /// <summary>The song's part is always there: it holds the share button.</summary>
    public bool ShowHeaderPart => true;

    // ---- Appearance (Settings > Mini player), each part independent of the others ----

    public MiniPlayerStyle Look => _settings.Current.MiniPlayerLook;

    public bool IsCinema => Look.Layout == MiniPlayerLayout.Cinema;

    /// <summary>From the right-click menus: vertical or cinema wide.</summary>
    public void SetLayout(MiniPlayerLayout layout) => _settings.Update(s => s.MiniPlayerLook.Layout = layout);

    public void SetBackground(MiniPlayerBackground background) => _settings.Update(s => s.MiniPlayerLook.Background = background);

    public System.Windows.TextAlignment LyricsTextAlignment =>
        Look.Alignment == LyricsAlignment.Left ? System.Windows.TextAlignment.Left : System.Windows.TextAlignment.Center;

    public System.Windows.Point LyricsScaleOrigin => Look.Alignment == LyricsAlignment.Left ? new(0, 0.5) : new(0.5, 0.5);

    /// <summary>Room at the sides for a line drawn larger.</summary>
    public System.Windows.Thickness LyricsLinePadding => Look.Highlight == LyricsHighlight.Expressive
        ? new(Look.Alignment == LyricsAlignment.Left ? 4 : 18, 6, 18, 6) : new(4, 5, 4, 5);

    public FontFamily LyricsFont => Look.Typeface switch
    {
        LyricsTypeface.Modern => Views.LyricCardRenderer.Figtree,
        LyricsTypeface.Serif => Views.LyricCardRenderer.DmSerif,
        _ => (FontFamily)System.Windows.Application.Current.FindResource("UiFont"),
    };

    public double LyricsFontSize => Math.Round(14 * Math.Clamp(Look.LyricsScale, 0.8, 1.4) * (IsCinema ? 1.12 : 1) * (Look.Typeface == LyricsTypeface.Serif ? 1.08 : 1), 1);

    public bool ShowProgress => L.ShowProgress;

    /// <summary>The window: the card (wide enough for every button) plus the 12 px each side its shadow needs.</summary>
    public double WindowWidth => (IsCinema ? Math.Clamp(L.Width + 200, 560, 720) : Math.Clamp(L.Width, 310, 640)) + 24;

    public bool IsShuffleActive => _monitor.IsShuffleActive;

    public bool IsRepeatActive => _monitor.RepeatMode != RepeatMode.None;

    public string RepeatGlyph => _monitor.RepeatMode == RepeatMode.Track ? "" : "";

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string Artist { get => _artist; private set => Set(ref _artist, value); }

    public ImageSource? Cover { get => _cover; private set => Set(ref _cover, value); }

    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }

    public bool CanSeek => _monitor.CanSeek;

    public double PositionSeconds { get => _positionSeconds; private set => Set(ref _positionSeconds, value); }

    public double DurationSeconds { get => _durationSeconds; private set => Set(ref _durationSeconds, value); }

    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }

    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }

    /// <summary>Shown instead of the lyric list when there are no synced lyrics.</summary>
    public string? StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    /// <summary>"Lyrics provided by Musixmatch", under the last line, for Spotify's lyrics.</summary>
    public string? Credit { get => _credit; private set => Set(ref _credit, value); }

    public int CurrentIndex { get => _currentIndex; private set => Set(ref _currentIndex, value); }

    /// <summary>Album colours, for the gradient background.</summary>
    public (Color Start, Color End) Palette => _monitor.Palette;

    /// <summary>Raised after the highlighted line changed, so the view can scroll to it.</summary>
    public event EventHandler? CurrentLineChanged;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!Set(ref _isActive, value))
                return;
            if (value)
            {
                LocalizationService.Changed += OnTrackChanged;
                _monitor.TrackChanged += OnTrackChanged;
                _monitor.PlaybackChanged += OnPlaybackChanged;
                _monitor.TimelineChanged += OnTimelineChanged;
                _monitor.ThumbnailChanged += OnThumbnailChanged;
                _sync.LyricsChanged += OnLyricsChanged;
                _sync.CurrentLineChanged += OnCurrentLineChanged;
                _settings.Changed += OnSettingsChanged;
                RefreshAll();
                OnSettingsChanged(this, EventArgs.Empty);
                Like.Refresh();
                _progressTimer.Start();
            }
            else
            {
                Volume.IsOpen = false;
                Like.IsConfirming = false;
                Unsubscribe();
                _progressTimer.Stop();
            }
        }
    }

    /// <summary>Seek from the progress bar.</summary>
    public void SeekTo(double seconds)
    {
        _previewSeconds = null;
        if (_monitor.CanSeek)
            // The monitor moves its clock before awaiting Spotify, so the bar shows the new spot at once.
            _ = _monitor.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, DurationSeconds)));
        UpdateProgress();
    }

    /// <summary>Arrow keys: a few seconds back or forward.</summary>
    public void SeekBy(double seconds)
    {
        if (_monitor.CanSeek)
            SeekTo(_monitor.Position.TotalSeconds + seconds);
    }

    /// <summary>While the progress bar is dragged, the time label follows the pointer (null: the song again).</summary>
    public void PreviewPosition(double? seconds)
    {
        _previewSeconds = seconds;
        UpdateProgress();
    }

    public void Dispose()
    {
        Like.Dispose();
        Unsubscribe();
        _progressTimer.Stop();
    }

    private void Unsubscribe()
    {
        LocalizationService.Changed -= OnTrackChanged;
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPlaybackChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _monitor.ThumbnailChanged -= OnThumbnailChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _sync.CurrentLineChanged -= OnCurrentLineChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnTrackChanged(object? sender, EventArgs e) => RefreshAll();

    /// <summary>The layout (and the shuffle/repeat state it shows) may differ.</summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        foreach (var name in new[]
        {
            nameof(ShowCover), nameof(ShowTitle), nameof(ShowArtist), nameof(ShowHeader), nameof(ShowShuffle), nameof(ShowPrevious),
            nameof(ShowPlayPause), nameof(ShowNext), nameof(ShowRepeat), nameof(ShowVolume), nameof(ShowLike), nameof(ShowControls), nameof(ShowProgress),
            nameof(ShowTransport), nameof(Look), nameof(IsCinema), nameof(LyricsTextAlignment), nameof(LyricsScaleOrigin), nameof(LyricsLinePadding),
            nameof(LyricsFont), nameof(LyricsFontSize),
            nameof(WindowWidth), nameof(IsShuffleActive), nameof(IsRepeatActive), nameof(RepeatGlyph),
        })
            OnPropertyChanged(name);
        if (ShowVolume)
            Volume.Refresh();
        UpdateCurrentLine();
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        IsPlaying = _monitor.IsPlaying;
        OnPropertyChanged(nameof(IsShuffleActive));
        OnPropertyChanged(nameof(IsRepeatActive));
        OnPropertyChanged(nameof(RepeatGlyph));
        OnPropertyChanged(nameof(ShowShuffle));
        OnPropertyChanged(nameof(ShowRepeat));
        OnPropertyChanged(nameof(ShowControls));
        OnPropertyChanged(nameof(CanSeek));
        CommandManager.InvalidateRequerySuggested();
        UpdateProgress();
    }

    private void OnTimelineChanged(object? sender, EventArgs e) => UpdateProgress();

    private void OnThumbnailChanged(object? sender, EventArgs e)
    {
        Cover = _monitor.Thumbnail;
        OnPropertyChanged(nameof(Palette));
    }

    private void OnLyricsChanged(object? sender, EventArgs e) => RebuildLines();

    private void OnCurrentLineChanged(object? sender, EventArgs e) => UpdateCurrentLine();

    private void RefreshAll()
    {
        var track = _monitor.Track;
        Title = track?.Title ?? LocalizationService.Get("NothingPlaying");
        Artist = track?.Artist ?? LocalizationService.Get("OpenSpotify");
        Cover = _monitor.Thumbnail;
        IsPlaying = _monitor.IsPlaying;
        OnPropertyChanged(nameof(CanSeek));
        RebuildLines();
        UpdateProgress();
    }

    private void RebuildLines()
    {
        Lines.Clear();
        _currentIndex = -1;
        if (_sync.Timeline is { } timeline)
        {
            for (var i = 0; i < timeline.Count; i++)
                Lines.Add(new LyricLineViewModel(i, timeline.Lines[i].Time, timeline.Lines[i].Text));
        }

        Credit = Lines.Count > 0 ? LyricsCredit.For(_sync.Result) : null;
        StatusMessage = _monitor.Track is null ? null : _sync.State switch
        {
            LyricsState.Synced => null,
            LyricsState.Loading => LocalizationService.Get("LoadingLyrics"),
            LyricsState.Instrumental => LocalizationService.Get("Instrumental"),
            LyricsState.PlainOnly => LocalizationService.Get("UnavailableLyrics"),
            LyricsState.Error => LocalizationService.Get("LyricsError"),
            _ => LocalizationService.Get("UnavailableLyrics"),
        };
        UpdateCurrentLine();
    }

    private void UpdateCurrentLine()
    {
        var index = _sync.CurrentIndex;
        var highlight = Look.Highlight;
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].IsCurrent = i == index;
            Lines[i].IsPast = i < index;
            Lines[i].Emphasize(highlight, i - Math.Max(index, 0), started: index >= 0);
        }
        CurrentIndex = index;
        CurrentLineChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress()
    {
        var duration = _monitor.Duration ?? TimeSpan.Zero;
        var position = _monitor.Position;
        DurationSeconds = Math.Max(1, duration.TotalSeconds);
        PositionSeconds = Math.Min(position.TotalSeconds, DurationSeconds);
        PositionText = Format(_previewSeconds is { } preview ? TimeSpan.FromSeconds(preview) : position);
        DurationText = duration > TimeSpan.Zero ? Format(duration) : "--:--";
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
