using Taskitify.Core.Lyrics;

namespace Taskitify.Linux;

/// <summary>Lets the Spotify source be turned on or off without rebuilding the lyrics session.</summary>
internal sealed class SwitchableLyricsService(ILyricsService current) : ILyricsService
{
    public ILyricsService Current { get; set; } = current;

    public IReadOnlyList<string> Sources => Current.Sources;

    public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct) => Current.GetLyricsAsync(query, ct);

    public Task ForgetAsync(TrackQuery query) => Current.ForgetAsync(query);

    public Task<LyricsResult> GetLyricsFromAsync(TrackQuery query, string source, CancellationToken ct) =>
        Current.GetLyricsFromAsync(query, source, ct);
}
