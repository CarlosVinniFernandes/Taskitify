using Taskitify.Core.Media;

namespace Taskitify.Core.Lyrics;

public enum LyricsSessionState
{
    None,
    Loading,
    Synced,
    PlainOnly,
    Instrumental,
    NotFound,
    Error,
}

/// <summary>
/// Follows an <see cref="IMediaSource"/> and keeps the lyric line for the current position:
/// looks the song up (debounced, cancelling stale lookups, retrying errors with backoff) and arms a
/// single timer for the instant of the next line instead of polling. No UI types, so it is shared
/// by every front end and tested without one. Events are raised on timer/thread-pool threads.
/// </summary>
public sealed class LyricsSession : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    private readonly IMediaSource _media;
    private readonly ILyricsService _lyrics;
    private readonly object _gate = new();
    private readonly Timer _lineTimer;
    private readonly Timer _retryTimer;
    private CancellationTokenSource? _loadCts;
    private TrackQuery? _query;
    private int _retryCount;
    private int _offsetMs;
    private bool _disposed;

    public LyricsSession(IMediaSource media, ILyricsService lyrics)
    {
        _media = media;
        _lyrics = lyrics;
        _lineTimer = new Timer(_ => Reschedule(), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new Timer(_ => _ = LoadAsync(debounce: false), null, Timeout.Infinite, Timeout.Infinite);
        _media.TrackChanged += OnTrackChanged;
        _media.PlaybackChanged += OnPositionBaseChanged;
        _media.TimelineChanged += OnTimelineChanged;
    }

    public TimeSpan LoadDebounce { get; init; } = TimeSpan.FromMilliseconds(400);

    public LyricsSessionState State { get; private set; } = LyricsSessionState.None;

    public LyricsTimeline? Timeline { get; private set; }

    public string? PlainText { get; private set; }

    public string? Source { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int CurrentIndex { get; private set; } = -1;

    public LyricsLine? CurrentLine => Timeline is { } t && CurrentIndex >= 0 ? t.Lines[CurrentIndex] : null;

    /// <summary>The user's lyric-timing correction: positive shows lines earlier.</summary>
    public int OffsetMs
    {
        get => _offsetMs;
        set
        {
            _offsetMs = value;
            Reschedule();
        }
    }

    public TimeSpan LyricsPosition => _media.Position + TimeSpan.FromMilliseconds(_offsetMs);

    /// <summary>State or lyrics changed (new song, loaded, not found...).</summary>
    public event EventHandler? LyricsChanged;

    public event EventHandler? CurrentLineChanged;

    /// <summary>The sources the current song can be looked up in one by one.</summary>
    public IReadOnlyList<string> Sources => _lyrics.Sources;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _media.TrackChanged -= OnTrackChanged;
        _media.PlaybackChanged -= OnPositionBaseChanged;
        _media.TimelineChanged -= OnTimelineChanged;
        _lineTimer.Dispose();
        _retryTimer.Dispose();
        _loadCts?.Cancel();
    }

    /// <summary>Forget whatever is cached for the current song and look it up again.</summary>
    public async Task ForceReloadAsync()
    {
        if (_media.Track is { } track)
            await _lyrics.ForgetAsync(ToQuery(track));
        if (_query is { } q)
            await _lyrics.ForgetAsync(q);
        _retryCount = 0;
        await LoadAsync(debounce: false);
    }

    /// <summary>Look the current song up in one source only; what it finds replaces the cached lyrics.</summary>
    public Task ReloadFromAsync(string source)
    {
        _retryCount = 0;
        return LoadAsync(debounce: false, source);
    }

    /// <summary>Start following the source's current song (call once after its StartAsync).</summary>
    public Task InitializeAsync() => LoadAsync(debounce: false);

    private static TrackQuery ToQuery(MediaTrack t) => new(t.Title, t.Artist, t.Album, t.Duration);

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        _retryCount = 0;
        _ = LoadAsync(debounce: true);
    }

    private void OnPositionBaseChanged(object? sender, EventArgs e) => Reschedule();

    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        // The duration is often unknown when the song starts; a lookup without it is less precise.
        if (_query is { } q && _media.Track?.Duration is { } d
            && State is LyricsSessionState.NotFound or LyricsSessionState.Error
            && (q.Duration is null || Math.Abs((q.Duration.Value - d).TotalSeconds) > TrackMatcher.DurationTolerance.TotalSeconds))
        {
            _ = LoadAsync(debounce: false);
            return;
        }
        Reschedule();
    }

    private async Task LoadAsync(bool debounce, string? onlySource = null)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
                return;
            _loadCts?.Cancel();
            _loadCts = cts = new CancellationTokenSource();
        }
        _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
        var ct = cts.Token;

        var track = _media.Track;
        if (track is null || string.IsNullOrWhiteSpace(track.Title) || _media.Status == MediaPlaybackStatus.NoSession)
        {
            _query = null;
            Apply(LyricsSessionState.None, null, null, null, null);
            return;
        }

        var query = ToQuery(track);
        _query = query;
        Apply(LyricsSessionState.Loading, null, null, null, null);

        try
        {
            if (debounce)
                await Task.Delay(LoadDebounce, ct).ConfigureAwait(false);
            // The cover/duration of the new song may have arrived during the debounce.
            query = ToQuery(_media.Track ?? track);
            _query = query;

            var result = onlySource is null
                ? await _lyrics.GetLyricsAsync(query, ct).ConfigureAwait(false)
                : await _lyrics.GetLyricsFromAsync(query, onlySource, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            switch (result.Kind)
            {
                case LyricsKind.Synced:
                    _retryCount = 0;
                    Apply(LyricsSessionState.Synced, new LyricsTimeline(result.Lines), null, result.Source, null);
                    break;
                case LyricsKind.PlainOnly:
                    _retryCount = 0;
                    Apply(LyricsSessionState.PlainOnly, null, result.PlainText, result.Source, null);
                    break;
                case LyricsKind.Instrumental:
                    _retryCount = 0;
                    Apply(LyricsSessionState.Instrumental, null, null, result.Source, null);
                    break;
                case LyricsKind.Error:
                    Apply(LyricsSessionState.Error, null, null, null, result.ErrorMessage);
                    ScheduleRetry();
                    break;
                default:
                    _retryCount = 0;
                    Apply(LyricsSessionState.NotFound, null, null, null, null);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer lookup replaced this one.
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                Apply(LyricsSessionState.Error, null, null, null, ex.Message);
                ScheduleRetry();
            }
        }
    }

    private void ScheduleRetry()
    {
        var delay = RetryDelays[Math.Min(_retryCount, RetryDelays.Length - 1)];
        _retryCount++;
        _retryTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void Apply(LyricsSessionState state, LyricsTimeline? timeline, string? plain, string? source, string? error)
    {
        State = state;
        Timeline = timeline;
        PlainText = plain;
        Source = source;
        ErrorMessage = error;
        CurrentIndex = -1;
        LyricsChanged?.Invoke(this, EventArgs.Empty);
        Reschedule();
    }

    /// <summary>Recompute the active line and arm the timer for the next change.</summary>
    private void Reschedule()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }
        var timeline = Timeline;
        if (timeline is null || State != LyricsSessionState.Synced)
        {
            _lineTimer.Change(Timeout.Infinite, Timeout.Infinite);
            SetIndex(-1);
            return;
        }

        var position = LyricsPosition;
        SetIndex(timeline.IndexAt(position));

        if (_media.Status != MediaPlaybackStatus.Playing)
        {
            _lineTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        if (timeline.NextChangeAfter(position) is { } next)
        {
            var wait = next - position;
            // Never spin: at least a few ms, and re-check at least every 5 s in case of drift.
            if (wait < TimeSpan.FromMilliseconds(5))
                wait = TimeSpan.FromMilliseconds(5);
            if (wait > TimeSpan.FromSeconds(5))
                wait = TimeSpan.FromSeconds(5);
            _lineTimer.Change(wait, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _lineTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void SetIndex(int index)
    {
        if (index == CurrentIndex)
            return;
        CurrentIndex = index;
        CurrentLineChanged?.Invoke(this, EventArgs.Empty);
    }
}
