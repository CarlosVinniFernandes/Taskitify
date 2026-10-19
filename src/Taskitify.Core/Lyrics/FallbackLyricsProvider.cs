using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Asks <c>primary</c> first and only asks <c>fallback</c> when the primary has no synced
/// lyrics, so the primary's version always wins and the fallback's rate limit is spared.
/// Plain lyrics from the primary beat plain lyrics from the fallback. A failure only surfaces
/// when neither source found anything, so it is retried instead of cached as "not found".
/// The retry that follows (LyricsSyncService) then asks only the source that failed.
/// </summary>
public sealed class FallbackLyricsProvider(ILyricsProvider primary, ILyricsProvider fallback, Func<DateTimeOffset>? now = null) : ILyricsProvider
{
    // Covers the retries of one song (LyricsSyncService backs off 10 s, 20 s, 40 s... 5 min).
    private static readonly TimeSpan AnswerTtl = TimeSpan.FromMinutes(10);
    private const int MaxRemembered = 64;

    // What each source answered (no synced lyrics) while the other one failed; the retry reuses it.
    private readonly ConcurrentDictionary<string, (LrclibRecord? Record, DateTimeOffset At)> _primaryAnswers = new();
    private readonly ConcurrentDictionary<string, (LrclibRecord? Record, DateTimeOffset At)> _fallbackAnswers = new();
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public string Name => $"{primary.Name}, then {fallback.Name}";

    public bool IsLocal => false;

    public IEnumerable<ILyricsProvider> Sources => primary.Sources.Concat(fallback.Sources);

    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        var key = LyricsCache.KeyFor(query);
        Exception? firstFailure = null;
        var primaryRemembered = Recall(_primaryAnswers, key, out var first);
        if (!primaryRemembered)
        {
            (first, firstFailure) = await TryFindAsync(primary, query, ct).ConfigureAwait(false);
            if (IsComplete(first))
                return first;
        }

        Exception? secondFailure = null;
        var fallbackRemembered = Recall(_fallbackAnswers, key, out var second);
        if (!fallbackRemembered)
            (second, secondFailure) = await TryFindAsync(fallback, query, ct).ConfigureAwait(false);
        // The primary did not answer: whatever the fallback has is only a stand-in until it does.
        var standIn = firstFailure is not null && second is not null ? second with { Provisional = true } : second;
        if (IsComplete(standIn))
            return standIn;

        var plain = HasLyrics(first) ? first : HasLyrics(standIn) ? standIn : null;
        if (plain is null && (firstFailure ?? secondFailure) is { } failure)
        {
            // A retry is coming; only the source that failed needs asking again. An answer keeps
            // the time its source gave it: retries reusing it must not stretch its lifetime.
            if (firstFailure is null && !primaryRemembered)
                Remember(_primaryAnswers, key, first);
            if (secondFailure is null && !fallbackRemembered)
                Remember(_fallbackAnswers, key, second);
            ExceptionDispatchInfo.Throw(failure);
        }
        return plain;
    }

    private static async Task<(LrclibRecord? Record, Exception? Failure)> TryFindAsync(
        ILyricsProvider provider, TrackQuery query, CancellationToken ct)
    {
        try
        {
            return (LyricsService.WithSource(await provider.FindAsync(query, ct).ConfigureAwait(false), provider), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Includes a provider's own timeout: the other source may still have lyrics.
            return (null, ex);
        }
    }

    /// <summary>A reload (or signing in to the primary) asks both sources again.</summary>
    public void Forget(TrackQuery query)
    {
        var key = LyricsCache.KeyFor(query);
        _primaryAnswers.TryRemove(key, out _);
        _fallbackAnswers.TryRemove(key, out _);
        primary.Forget(query);
        fallback.Forget(query);
    }

    private bool Recall(ConcurrentDictionary<string, (LrclibRecord? Record, DateTimeOffset At)> answers, string key, out LrclibRecord? record)
    {
        var fresh = answers.TryGetValue(key, out var answer) && _now() - answer.At < AnswerTtl;
        record = fresh ? answer.Record : null;
        return fresh;
    }

    private void Remember(ConcurrentDictionary<string, (LrclibRecord? Record, DateTimeOffset At)> answers, string key, LrclibRecord? record)
    {
        if (answers.Count >= MaxRemembered)
            answers.Clear();
        answers[key] = (record, _now());
    }

    private static bool IsComplete(LrclibRecord? r) =>
        LyricsService.FromRecord(r).Kind is LyricsKind.Synced or LyricsKind.Instrumental;

    private static bool HasLyrics(LrclibRecord? r) => LyricsService.FromRecord(r).Kind != LyricsKind.NotFound;
}
