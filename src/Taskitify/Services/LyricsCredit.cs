using Taskitify.Core.Lyrics;

namespace Taskitify.Services;

/// <summary>The line crediting who licenses the lyrics, where they are shown.</summary>
public static class LyricsCredit
{
    /// <summary>
    /// "Lyrics provided by Musixmatch" for lyrics that came from Spotify (Spotify names its
    /// partner; older cached ones did not keep it, and Spotify's partner is Musixmatch);
    /// null for other sources.
    /// </summary>
    public static string? For(LyricsResult? result) =>
        result is { Source: "Spotify", Kind: LyricsKind.Synced or LyricsKind.PlainOnly }
            ? LocalizationService.Format("LyricsCredit", string.IsNullOrWhiteSpace(result.Credit) ? "Musixmatch" : result.Credit)
            : null;
}
