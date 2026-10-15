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
    private int _currentIndex = -1;

    public MiniPlayerViewModel(ISpotifyMonitor monitor, LyricsSyncService sync)
    {
        _monitor = monitor;
        _sync = sync;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        PlayPauseCommand = new RelayCommand(() => _ = _monitor.PlayPauseAsync(), () => _monitor.HasSession);
        NextCommand = new RelayCommand(() => _ = _monitor.NextAsync(), () => _monitor.CanGoNext);
        PreviousCommand = new RelayCommand(() => _ = _monitor.PreviousAsync(), () => _monitor.CanGoPrevious);
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
                _monitor.TrackChanged += OnTrackChanged;
                _monitor.PlaybackChanged += OnPlaybackChanged;
                _monitor.TimelineChanged += OnTimelineChanged;
                _monitor.ThumbnailChanged += OnThumbnailChanged;
                _sync.LyricsChanged += OnLyricsChanged;
                _sync.CurrentLineChanged += OnCurrentLineChanged;
                RefreshAll();
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
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPlaybackChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _monitor.ThumbnailChanged -= OnThumbnailChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _sync.CurrentLineChanged -= OnCurrentLineChanged;
    }

    private void OnTrackChanged(object? sender, EventArgs e) => RefreshAll();

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        IsPlaying = _monitor.IsPlaying;
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
        Title = track?.Title ?? "Nada tocando";
        Artist = track?.Artist ?? "Abra o Spotify para começar";
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
            LyricsState.Loading => "Buscando letra…",
            LyricsState.Instrumental => "Faixa instrumental",
            LyricsState.PlainOnly => "Não há letra sincronizada disponível para esta música.",
            LyricsState.Error => "Não foi possível buscar a letra agora. Nova tentativa em instantes.",
            _ => "Não há letra sincronizada disponível para esta música.",
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
