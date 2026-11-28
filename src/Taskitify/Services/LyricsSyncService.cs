using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;

namespace Taskitify.Services;

/// <summary>Whether the song playing has karaoke (word- or syllable-timed) lyrics, as far as we know.</summary>
public enum KaraokeAvailability
{
    /// <summary>Not looked for: karaoke search is off, or nothing plays.</summary>
    Off,

    /// <summary>Looked for only on request (automatic search is off): the button asks.</summary>
    NotSearched,

    Searching,

    Unavailable,

    Available,
}

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
    private LyricFlow? _flow;
    private TimeSpan? _flowDuration;
    private readonly KaraokeService? _karaoke;
    private CancellationTokenSource? _karaokeCts;
    // The song's usual (line-timed) lyrics, kept while karaoke lyrics are shown instead.
    private LyricsTimeline? _lineTimeline;
    private LyricsState _lineState;
    private (bool Search, bool On, string Sources) _karaokePrefs;

    public LyricsSyncService(ISpotifyMonitor monitor, ILyricsService lyrics, SettingsService settings, KaraokeService? karaoke = null)
    {
        _monitor = monitor;
        _lyrics = lyrics;
        _settings = settings;
        _karaoke = karaoke;
        _karaokePrefs = KaraokePrefs();
        _lineTimer = new DispatcherTimer(DispatcherPriority.Render);
        _lineTimer.Tick += (_, _) => Reschedule(fromBoundaryTimer: true);
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
        _settings.Changed += OnKaraokeSettingsChanged;
    }

    /// <summary>Whether the song has karaoke lyrics (looked for after the usual lyrics are shown).</summary>
    public KaraokeAvailability KaraokeAvailability { get; private set; }

    /// <summary>The song's karaoke lyrics when found (whether shown or not).</summary>
    public KaraokeLyrics? Karaoke { get; private set; }

    /// <summary>Karaoke lyrics are what is shown now: available, and wanted (or the only lyrics there are).</summary>
    public bool KaraokeActive { get; private set; }

    /// <summary>The karaoke availability, the lyrics found or whether they are shown changed.</summary>
    public event EventHandler? KaraokeChanged;

    /// <summary>The karaoke button: turns karaoke on or off for every song (a saved preference); looks for it if not yet.</summary>
    public void ToggleKaraoke()
    {
        if (KaraokeAvailability == KaraokeAvailability.NotSearched)
        {
            _settings.Update(s => s.LyricsSources.KaraokeOn = true);
            StartKaraokeSearch(force: true);
            return;
        }
        if (KaraokeAvailability != KaraokeAvailability.Available)
            return;
        _settings.Update(s => s.LyricsSources.KaraokeOn = !s.LyricsSources.KaraokeOn);
    }

    /// <summary>How much of the line's backing vocals (as one) is sung.</summary>
    public LyricFill BackgroundFill(int index) =>
        KaraokeActive && Timeline is { } t && (uint)index < (uint)t.Count && LyricsVocal.Combine(t.Lines[index].Background) is { } vocal
            ? LyricFlow.ForVocal(vocal, LyricsPosition)
            : LyricFill.Full;

    public LyricsState State { get; private set; } = LyricsState.None;

    public LyricsResult? Result { get; private set; }

    public LyricsTimeline? Timeline { get; private set; }

    public int CurrentIndex { get; private set; } = -1;

    public LyricsLine? CurrentLine => Timeline is { } t && CurrentIndex >= 0 ? t.Lines[CurrentIndex] : null;

    /// <summary>
    /// Position used for lyrics: playback position plus the user's adjustments (all songs, and this
    /// one). The one place they are applied, so the bar, the mini player and Minimal Mode agree.
    /// </summary>
    public TimeSpan LyricsPosition => _monitor.Position + TimeSpan.FromMilliseconds(EffectiveOffsetMs);

    /// <summary>The song playing's identity for its own adjustment (null: nothing playing).</summary>
    public string? TrackKey => _monitor.Track is { } t ? AppSettings.TrackKey(t.Title, t.Artist) : null;

    public int EffectiveOffsetMs => _settings.Current.EffectiveOffsetMs(TrackKey);

    public bool IsPlaying => _monitor.IsPlaying;
    public double PlaybackRate => _monitor.PlaybackRate;
    public bool ChangedAtLineBoundary { get; private set; }

    public LyricFill Fill(int index, LyricFlowMode mode)
    {
        // Karaoke shows its real timing even with the usual (estimated) fill off: that is what it is for.
        var estimate = _settings.Current.LyricsSources.AllowEstimatedFill;
        if (KaraokeActive)
        {
            estimate &= mode != LyricFlowMode.Off;
            mode = LyricFlowMode.Automatic;
        }
        if (mode == LyricFlowMode.Off || Timeline is not { } timeline)
            return LyricFill.Full;
        if (_flow is null || _flowDuration != _monitor.Duration)
        {
            _flowDuration = _monitor.Duration;
            _flow = new LyricFlow(timeline, _flowDuration);
        }
        return _flow.At(index, LyricsPosition, mode, estimate);
    }

    public TimeSpan? NextChangeAfterCurrent => CurrentLine is { } line ? Timeline?.NextChangeAfter(line.Time) : null;

    /// <summary>State or lyrics changed (new song, loaded, not found...).</summary>
    public event EventHandler? LyricsChanged;

    public event EventHandler? CurrentLineChanged;

    public void Dispose()
    {
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPositionBaseChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _settings.Changed -= OnPositionBaseChanged;
        _settings.Changed -= OnKaraokeSettingsChanged;
        _lineTimer.Stop();
        _retryTimer.Stop();
        _loadCts?.Cancel();
        _karaokeCts?.Cancel();
    }

    /// <summary>Re-fetch the current track (after clearing the cache, for instance).</summary>
    public void Reload() => _ = LoadAsync(debounce: false);

    /// <summary>
    /// The line for the position now, if the current one is not it (a missed timer, a position
    /// report that arrived late): self-correction while a view follows the lyrics closely.
    /// </summary>
    public void EnsureCurrent()
    {
        if (Timeline is { Count: > 0 } timeline && timeline.IndexAt(LyricsPosition) != CurrentIndex)
        {
            Log.Debug($"Lyrics line was {CurrentIndex}, position says {timeline.IndexAt(LyricsPosition)}: resynced");
            Reschedule();
        }
    }

    /// <summary>Forget whatever is cached for the current song and look it up again.</summary>
    public async Task ForceReloadAsync()
    {
        if (_monitor.Track is { } track)
        {
            await _lyrics.ForgetAsync(new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration));
            _karaoke?.Forget(new TrackQuery(track.Title, track.Artist, track.Album, _monitor.Duration));
            if (_query is { } q)
            {
                await _lyrics.ForgetAsync(q);
                _karaoke?.Forget(q);
            }
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
        _lineTimeline = null;
        _flow = null;
        ChangedAtLineBoundary = false;
        _query = null;
        ResetKaraoke();
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
            _lineTimeline = Timeline;
            _lineState = State;
            Reschedule();
            if (State != LyricsState.Error)
                StartKaraokeSearch(force: false);
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

    private void Reschedule(bool fromBoundaryTimer = false)
    {
        _lineTimer.Stop();
        if (Timeline is not { Count: > 0 } timeline)
            return;

        var position = LyricsPosition;
        var index = timeline.IndexAt(position);
        ChangedAtLineBoundary = fromBoundaryTimer && _monitor.IsPlaying && index == CurrentIndex + 1 && CurrentIndex >= 0
            && position - timeline.Lines[index].Time <= TimeSpan.FromMilliseconds(125 * PlaybackRate);
        SetIndex(index);

        if (!_monitor.IsPlaying)
            return;

        var next = timeline.NextChangeAfter(position);
        var wait = next is { } n ? (n - position) / PlaybackRate : MaxTimerInterval;
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

    // ------------------------------------------------------------------ karaoke

    private (bool Search, bool On, string Sources) KaraokePrefs()
    {
        var k = _settings.Current.LyricsSources;
        return (k.KaraokeSearch, k.KaraokeOn, string.Join(',', k.EnabledKaraoke()));
    }

    private void ResetKaraoke()
    {
        _karaokeCts?.Cancel();
        _karaokeCts = null;
        Karaoke = null;
        KaraokeActive = false;
        KaraokeAvailability = KaraokeAvailability.Off;
        KaraokeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// After the usual lyrics are shown: the song's own syllable timing if its lyrics have it (no
    /// request), else a lookup in the karaoke sources, which never delays the lyrics.
    /// </summary>
    private void StartKaraokeSearch(bool force)
    {
        var settings = _settings.Current.LyricsSources;
        if (_monitor.Track is not { } track || _query is not { } query || (!settings.KaraokeSearch && !force))
        {
            SetKaraoke(KaraokeAvailability.Off, null);
            return;
        }
        if (Result is { Kind: LyricsKind.Synced, Lines: var lines } result && lines.Any(l => l.Anchors is { Count: > 0 }))
        {
            SetKaraoke(KaraokeAvailability.Available, new KaraokeLyrics(lines, LyricsSyncLevel.Word, result.Source ?? "") { Credit = result.Credit });
            return;
        }
        if (State == LyricsState.Instrumental || _karaoke is null)
        {
            SetKaraoke(KaraokeAvailability.Unavailable, null);
            return;
        }
        if (!settings.KaraokeAutoSearch && !force)
        {
            SetKaraoke(KaraokeAvailability.NotSearched, null);
            return;
        }
        _karaokeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _karaokeCts = cts;
        SetKaraoke(KaraokeAvailability.Searching, null);
        _ = LookUpKaraokeAsync(track, query, cts);
    }

    private async Task LookUpKaraokeAsync(TrackInfo track, TrackQuery query, CancellationTokenSource cts)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var found = await _karaoke!.FindAsync(new KaraokeQuery(query), cts.Token);
            // A late answer for a song no longer playing, or a lookup replaced by a newer one.
            if (cts.IsCancellationRequested || _monitor.Track != track || !ReferenceEquals(_karaokeCts, cts))
                return;
            Log.Info($"Karaoke for '{track.Title}': {(found is null ? "none" : $"{found.Sync} from {found.Source}{(found.HasBackground ? ", backing vocals" : "")}{(found.HasDuet ? ", duet" : "")}")} in {clock.Elapsed.TotalSeconds:0.00} s");
            SetKaraoke(found is null ? KaraokeAvailability.Unavailable : KaraokeAvailability.Available, found);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Karaoke lookup failed", ex);
            if (!cts.IsCancellationRequested && ReferenceEquals(_karaokeCts, cts))
                SetKaraoke(KaraokeAvailability.Unavailable, null);
        }
    }

    private void SetKaraoke(KaraokeAvailability availability, KaraokeLyrics? lyrics)
    {
        KaraokeAvailability = availability;
        Karaoke = lyrics;
        ApplyKaraoke();
        KaraokeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the karaoke lyrics or the usual ones, as wanted. Switching keeps the position: the
    /// line for the position now is current at once. Karaoke lyrics also fill in for a song
    /// with no synced lyrics at all, whatever the preference.
    /// </summary>
    private void ApplyKaraoke()
    {
        var wanted = _settings.Current.LyricsSources.KaraokeOn;
        var use = Karaoke is not null && (wanted || _lineTimeline is null);
        var timeline = use ? new LyricsTimeline(KaraokeLyrics.WithInterludes(Karaoke!.Lines)) : _lineTimeline;
        var active = use && wanted;
        if (ReferenceEquals(timeline, Timeline) && active == KaraokeActive)
            return;
        var changedLines = !ReferenceEquals(timeline, Timeline);
        KaraokeActive = active;
        if (!changedLines)
            return;
        Timeline = timeline;
        _flow = null;
        ChangedAtLineBoundary = false;
        if (use && State != LyricsState.Synced)
            State = LyricsState.Synced;
        else if (!use)
            State = _lineState;
        SetIndex(-1, force: true);
        LyricsChanged?.Invoke(this, EventArgs.Empty);
        Reschedule();
    }

    private void OnKaraokeSettingsChanged(object? sender, EventArgs e)
    {
        var prefs = KaraokePrefs();
        if (prefs == _karaokePrefs)
            return;
        var before = _karaokePrefs;
        _karaokePrefs = prefs;
        if (_query is null)
            return;
        // Search turned on, or the sources changed while none was found: look again.
        if ((prefs.Search && !before.Search) || (prefs.Sources != before.Sources && KaraokeAvailability is KaraokeAvailability.Unavailable or KaraokeAvailability.Off))
            StartKaraokeSearch(force: false);
        else if (!prefs.Search && before.Search)
        {
            _karaokeCts?.Cancel();
            SetKaraoke(KaraokeAvailability.Off, null);
        }
        else
        {
            ApplyKaraoke();
            KaraokeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
