using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// Conservative matching between the playing track and an LRCLIB record. Lyrics are only
/// accepted when title and artist agree after normalization and the duration is close,
/// so an ambiguous search hit is never shown as the current song's lyrics.
/// </summary>
public static partial class TrackMatcher
{
    // Spotify's reported length and LRCLIB's often differ by a few seconds for the very same
    // recording (observed: 271.7 s vs 276 s). Live/extended/radio versions usually differ by
    // far more, and the closest candidate is always preferred.
    public static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(8);

    // " - Remastered 2011", " - 2009 Remaster", " - Digital Remaster"
    [GeneratedRegex(@"\s+[-–—]\s+(\d{4}\s+)?(digital(ly)?\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemasterSuffix();

    // "(Remastered 2015)", "[feat. X]", "(with Y)", "(ft. Z)"
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(remaster(ed)?|feat\.?|ft\.|with)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex DecorationGroup();

    // " - feat. X" / " feat. X" at the end
    [GeneratedRegex(@"\s+([-–—]\s+)?(feat\.?|ft\.)\s+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatSuffix();

    [GeneratedRegex(@"\s*(,|&|\bx\b|\bfeat\.?|\bft\.|;|/)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparators();

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;
        var t = DecorationGroup().Replace(title, string.Empty);
        t = RemasterSuffix().Replace(t, string.Empty);
        t = FeatSuffix().Replace(t, string.Empty);
        return NormalizeText(t);
    }

    /// <summary>Lowercase, no diacritics, "&amp;" as "and", punctuation removed, single spaces.</summary>
    public static string NormalizeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        var decomposed = s.Replace("&", " and ").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastSpace = false;
            }
            else if (!lastSpace && (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)))
            {
                // Apostrophes join words ("don't" == "dont"); everything else separates them.
                if (c is '\'' or '’')
                    continue;
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> SplitArtists(string? artists)
    {
        if (string.IsNullOrWhiteSpace(artists))
            return [];
        return ArtistSeparators().Split(artists)
            .Select(NormalizeText)
            .Where(a => a.Length > 0 && a is not ("feat" or "ft" or "x"))
            .Distinct()
            .ToList();
    }

    public static bool TitlesMatch(string? a, string? b)
    {
        var na = NormalizeTitle(a);
        return na.Length > 0 && na == NormalizeTitle(b);
    }

    public static bool ArtistsMatch(string? a, string? b)
    {
        var na = NormalizeText(a);
        var nb = NormalizeText(b);
        if (na.Length == 0 || nb.Length == 0)
            return false;
        if (na == nb)
            return true;
        var sa = SplitArtists(a);
        var sb = SplitArtists(b);
        return sa.Intersect(sb).Any();
    }

    public static bool DurationsMatch(TimeSpan? expected, double? candidateSeconds)
    {
        if (expected is not { } e || e <= TimeSpan.Zero || candidateSeconds is not { } c || c <= 0)
            return true; // unknown on either side: title + artist must carry the match
        return Math.Abs(e.TotalSeconds - c) <= DurationTolerance.TotalSeconds;
    }

    public static bool IsMatch(TrackQuery query, LrclibRecord record) =>
        TitlesMatch(query.Title, record.TrackName)
        && ArtistsMatch(query.Artist, record.ArtistName)
        && DurationsMatch(query.Duration, record.Duration);

    /// <summary>Distance in seconds used to rank otherwise-equal candidates.</summary>
    public static double DurationDistance(TrackQuery query, LrclibRecord record) =>
        query.Duration is { } d && record.Duration is { } c ? Math.Abs(d.TotalSeconds - c) : double.MaxValue / 2;
}
