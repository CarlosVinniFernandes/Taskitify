using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;

namespace Taskitify.Services;

/// <summary>
/// Fetches lyrics for the current track and tracks the active line. Instead of polling at a
/// fixed rate, a single timer is armed for the exact moment the next line starts and re-armed
/// whenever the playback position base changes (seek, pause, resume, offset change).
/// </summary>
public sealed class LyricsSyncService : IDisposable
{
    private static readonly TimeSpan LoadDebounce = TimeSpan.FromMilliseconds(350);
    // The client already retries for ~1 min; after that, try again soon while the song plays.
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxTimerInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DurationWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DurationPoll = TimeSpan.FromMilliseconds(250);

    private readonly ISpotifyMonitor _monitor;
    private readonly ILyricsService _lyrics;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _lineTimer;
    private readonly DispatcherTimer _retryTimer;

    private CancellationTokenSource? _loadCts;
    private TrackQuery? _query;
    private int _retryCount;

    public LyricsSyncService(ISpotifyMonitor monitor, ILyricsService lyrics, SettingsService settings)
    {
        _monitor = monitor;
        _lyrics = lyrics;
        _settings = settings;
        _lineTimer = new DispatcherTimer(DispatcherPriority.Render);
        _lineTimer.Tick += (_, _) => Reschedule();
        _retryTimer = new DispatcherTimer();
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            if (State == LyricsState.Error)
                _ = LoadAsync(debounce: false);
        };

        _monitor.TrackChanged += OnTrackChanged;
        _monitor.PlaybackChanged += OnPositionBaseChanged;
        _monitor.TimelineChanged += OnTimelineChanged;
        _settings.Changed += OnPositionBaseChanged;
    }

    public LyricsState State { get; private set; } = LyricsState.None;

    public LyricsResult? Result { get; private set; }

    public LyricsTimeline? Timeline { get; private set; }

    public int CurrentIndex { get; private set; } = -1;

    public LyricsLine? CurrentLine => Timeline is { } t && CurrentIndex >= 0 ? t.Lines[CurrentIndex] : null;

    /// <summary>Position used for lyrics: playback position plus the user's offset.</summary>
    public TimeSpan LyricsPosition => _monitor.Position + TimeSpan.FromMilliseconds(_settings.Current.SyncOffsetMs);

    /// <summary>State or lyrics changed (new song, loaded, not found...).</summary>
    public event EventHandler? LyricsChanged;

    public event EventHandler? CurrentLineChanged;

    public void Dispose()
    {
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPositionBaseChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _settings.Changed -= OnPositionBaseChanged;
        _lineTimer.Stop();
        _retryTimer.Stop();
        _loadCts?.Cancel();
    }

    /// <summary>Re-fetch the current track (after clearing the cache, for instance).</summary>
    public void Reload() => _ = LoadAsync(debounce: false);

    /// <summary>Forget whatever is cached for the current song and look it up again.</summary>
    public async Task ForceReloadAsync()
    {
        if (_monitor.Track is { } track)
        {
            await _lyrics.ForgetAsync(new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration));
            if (_query is { } q)
                await _lyrics.ForgetAsync(q);
        }
        _retryCount = 0;
        Reload();
    }

    /// <summary>The sources the current song can be looked up in one by one (the overlay's menu).</summary>
    public IReadOnlyList<string> Sources => _lyrics.Sources;

    /// <summary>Look the current song up in one source only; what it finds replaces the cached lyrics.</summary>
    public Task ReloadFromAsync(string source)
    {
        _retryCount = 0;
        return LoadAsync(debounce: false, source);
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        _retryCount = 0;
        _ = LoadAsync(debounce: true);
    }

    private void OnPositionBaseChanged(object? sender, EventArgs e) => Reschedule();

    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        // The duration is often unknown when the song starts; a lookup without it is less precise.
        if (_query is { } q && _monitor.Duration is { } d && State != LyricsState.Loading
            && (q.Duration is null || Math.Abs((q.Duration.Value - d).TotalSeconds) > TrackMatcher.DurationTolerance.TotalSeconds)
            && State is LyricsState.NotFound or LyricsState.Error)
        {
            _ = LoadAsync(debounce: false);
            return;
        }
        Reschedule();
    }

    /// <param name="onlySource">Only this source, skipping the caches (null: the usual lookup).</param>
    private async Task LoadAsync(bool debounce, string? onlySource = null)
    {
        _loadCts?.Cancel();
        _retryTimer.Stop();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        Result = null;
        Timeline = null;
        _query = null;
        SetIndex(-1, force: true);

        if (_monitor.Track is not { } track)
        {
            SetState(LyricsState.None);
            _lineTimer.Stop();
            return;
        }

        SetState(LyricsState.Loading);
        try
        {
            // Skipping quickly through songs only queries the one the user stays on.
            if (debounce)
                await Task.Delay(LoadDebounce, cts.Token);

            if (_monitor.Track != track)
                return;

            // Spotify only refreshes the timeline every few seconds; wait briefly for the new
            // song's duration, which makes matching much more precise.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var waited = TimeSpan.Zero; _monitor.Duration is null && waited < DurationWait; waited += DurationPoll)
                await Task.Delay(DurationPoll, cts.Token);
            if (_monitor.Track != track)
                return;
            var durationWait = clock.Elapsed;

            var query = new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration);
            _query = query;
            var result = onlySource is null
                ? await _lyrics.GetLyricsAsync(query, cts.Token)
                : await _lyrics.GetLyricsFromAsync(query, onlySource, cts.Token);
            if (cts.IsCancellationRequested || _monitor.Track != track)
                return;

            Result = result;
            Timeline = result.Kind == LyricsKind.Synced ? new LyricsTimeline(result.Lines) : null;
            // The source and timings let the debug log measure Spotify/LRCLIB/cache hit rates.
            var source = result.Source is null ? "" : $" from {result.Source}{(result.FromCache ? " (cache)" : "")}";
            Log.Info($"Lyrics for '{track.Title}': {result.Kind}{source} in {(clock.Elapsed - durationWait).TotalSeconds:0.00} s"
                + $" (+{durationWait.TotalSeconds:0.00} s waiting for the duration){(result.ErrorMessage is null ? "" : $" ({result.ErrorMessage})")}");
            SetState(result.Kind switch
            {
                LyricsKind.Synced => LyricsState.Synced,
                LyricsKind.PlainOnly => LyricsState.PlainOnly,
                LyricsKind.Instrumental => LyricsState.Instrumental,
                LyricsKind.NotFound => LyricsState.NotFound,
                _ => LyricsState.Error,
            });
            if (State == LyricsState.Error)
                ScheduleRetry();
            else
                _retryCount = 0;
            Reschedule();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Lyrics lookup failed", ex);
            if (!cts.IsCancellationRequested)
            {
                SetState(LyricsState.Error);
                ScheduleRetry();
            }
        }
    }

    /// <summary>Exponential backoff (10 s, 20 s, 40 s... up to 5 min): LRCLIB rate-limits with 429.</summary>
    private void ScheduleRetry()
    {
        var seconds = Math.Min(MaxRetry.TotalSeconds, FirstRetry.TotalSeconds * Math.Pow(2, _retryCount));
        _retryCount++;
        _retryTimer.Interval = TimeSpan.FromSeconds(seconds);
        _retryTimer.Start();
        Log.Info($"Retrying lyrics in {seconds:0} s");
    }

    private void Reschedule()
    {
        _lineTimer.Stop();
        if (Timeline is not { Count: > 0 } timeline)
            return;

        var position = LyricsPosition;
        SetIndex(timeline.IndexAt(position));

        if (!_monitor.IsPlaying)
            return;

        var next = timeline.NextChangeAfter(position);
        var wait = next is { } n ? n - position : MaxTimerInterval;
        if (wait > MaxTimerInterval)
            wait = MaxTimerInterval;
        if (wait < TimeSpan.FromMilliseconds(15))
            wait = TimeSpan.FromMilliseconds(15);
        _lineTimer.Interval = wait;
        _lineTimer.Start();
    }

    private void SetIndex(int index, bool force = false)
    {
        if (index == CurrentIndex && !force)
            return;
        CurrentIndex = index;
        CurrentLineChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(LyricsState state)
    {
        State = state;
        LyricsChanged?.Invoke(this, EventArgs.Empty);
    }
}
