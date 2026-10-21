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
    private readonly AppVolumeService _volume;
    private readonly DispatcherTimer _progressTimer;
    private string _volumeGlyph = "";
    private string _volumeTip = string.Empty;

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
    private int _currentIndex = -1;

    public MiniPlayerViewModel(ISpotifyMonitor monitor, LyricsSyncService sync, SettingsService settings, AppVolumeService volume)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        _volume = volume;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        PlayPauseCommand = new RelayCommand(() => _ = _monitor.PlayPauseAsync(), () => _monitor.HasSession);
        NextCommand = new RelayCommand(() => _ = _monitor.NextAsync(), () => _monitor.CanGoNext);
        PreviousCommand = new RelayCommand(() => _ = _monitor.PreviousAsync(), () => _monitor.CanGoPrevious);
        ShuffleCommand = new RelayCommand(() => _ = _monitor.ToggleShuffleAsync());
        RepeatCommand = new RelayCommand(() => _ = _monitor.CycleRepeatAsync());
        MuteCommand = new RelayCommand(() =>
        {
            _volume.ToggleMute();
            RefreshVolume();
        });
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

    public ICommand MuteCommand { get; }

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

    public bool ShowControls => ShowShuffle || L.ShowPrevious || L.ShowPlayPause || L.ShowNext || ShowRepeat || L.ShowVolume;

    public bool ShowProgress => L.ShowProgress;

    /// <summary>The window: the card (wide enough for every button) plus the 12 px each side its shadow needs.</summary>
    public double WindowWidth => Math.Clamp(L.Width, 310, 640) + 24;

    public bool IsShuffleActive => _monitor.IsShuffleActive;

    public bool IsRepeatActive => _monitor.RepeatMode != RepeatMode.None;

    public string RepeatGlyph => _monitor.RepeatMode == RepeatMode.Track ? "" : "";

    public string VolumeGlyph { get => _volumeGlyph; private set => Set(ref _volumeGlyph, value); }

    public string VolumeTip { get => _volumeTip; private set => Set(ref _volumeTip, value); }

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
        if (_volume.Get() is not { } state)
        {
            VolumeGlyph = "";
            VolumeTip = LocalizationService.Get("VolumeUnavailable");
            return;
        }
        VolumeGlyph = state.Muted || state.Volume <= 0.001f ? ""
            : state.Volume < 0.34f ? ""
            : state.Volume < 0.67f ? ""
            : "";
        VolumeTip = state.Muted ? LocalizationService.Get("SpotifyMuted") : LocalizationService.Format("VolumeDetail", Math.Round(state.Volume * 100));
    }

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
                _progressTimer.Start();
            }
            else
            {
                Unsubscribe();
                _progressTimer.Stop();
            }
        }
    }

    /// <summary>Seek from the progress bar.</summary>
    public void SeekTo(double seconds)
    {
        if (_monitor.CanSeek)
            _ = _monitor.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, DurationSeconds)));
    }

    public void Dispose()
    {
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
            nameof(ShowPlayPause), nameof(ShowNext), nameof(ShowRepeat), nameof(ShowVolume), nameof(ShowControls), nameof(ShowProgress),
            nameof(WindowWidth), nameof(IsShuffleActive), nameof(IsRepeatActive), nameof(RepeatGlyph),
        })
            OnPropertyChanged(name);
        if (ShowVolume)
            RefreshVolume();
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
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].IsCurrent = i == index;
            Lines[i].IsPast = i < index;
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
        PositionText = Format(position);
        DurationText = duration > TimeSpan.Zero ? Format(duration) : "--:--";
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
