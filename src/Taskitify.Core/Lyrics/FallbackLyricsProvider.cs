using System.Runtime.ExceptionServices;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Asks <c>primary</c> first and only asks <c>fallback</c> when the primary has no synced
/// lyrics, so the primary's version always wins and the fallback's rate limit is spared.
/// Plain lyrics from the primary beat plain lyrics from the fallback. A failure only surfaces
/// when neither source found anything, so it is retried instead of cached as "not found".
/// </summary>
public sealed class FallbackLyricsProvider(ILyricsProvider primary, ILyricsProvider fallback) : ILyricsProvider
{
    public string Name => $"{primary.Name}, then {fallback.Name}";

    public bool IsLocal => false;

    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        var (first, firstFailure) = await TryFindAsync(primary, query, ct).ConfigureAwait(false);
        if (IsComplete(first))
            return first;

        var (second, secondFailure) = await TryFindAsync(fallback, query, ct).ConfigureAwait(false);
        if (IsComplete(second))
            return second;

        var plain = HasLyrics(first) ? first : HasLyrics(second) ? second : null;
        if (plain is null && (firstFailure ?? secondFailure) is { } failure)
            ExceptionDispatchInfo.Throw(failure);
        return plain;
    }

    private static async Task<(LrclibRecord? Record, Exception? Failure)> TryFindAsync(
        ILyricsProvider provider, TrackQuery query, CancellationToken ct)
    {
        try
        {
            return (await provider.FindAsync(query, ct).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Includes a provider's own timeout: the other source may still have lyrics.
            return (null, ex);
        }
    }

    private static bool IsComplete(LrclibRecord? r) =>
        LyricsService.FromRecord(r).Kind is LyricsKind.Synced or LyricsKind.Instrumental;

    private static bool HasLyrics(LrclibRecord? r) => LyricsService.FromRecord(r).Kind != LyricsKind.NotFound;
}
