using System.Globalization;
using System.Text.RegularExpressions;

namespace Taskitify.Core.Lyrics;

/// <summary>
/// The user's own .lrc files in one folder (subfolders included). A file matches when its
/// [ar:]/[ti:] tags, or else its name ("Artist - Title.lrc"), agree with the playing track
/// under <see cref="TrackMatcher"/>; a [length:] tag is checked like LRCLIB's duration.
/// </summary>
public sealed partial class LocalLyricsProvider : ILyricsProvider
{
    // Larger than any real lyric; keeps a stray big file from slowing every lookup.
    private const long MaxFileSize = 512 * 1024;

    // Tags sit at the top of the file; this is plenty to find them without reading it all.
    private const int HeaderChars = 4096;

    private readonly string _directory;

    public LocalLyricsProvider(string directory) => _directory = directory;

    public string Name => "Local files";

    public bool IsLocal => true;

    public string Directory => _directory;

    [GeneratedRegex(@"^\[(ti|ar|length):\s*(.*?)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HeaderTag();

    [GeneratedRegex(@"^\[[^\]]*\]")]
    private static partial Regex LeadingTag();

    public async Task<LrclibRecord?> FindAsync(TrackQuery query, CancellationToken ct)
    {
        if (!System.IO.Directory.Exists(_directory))
            return null;

        IEnumerable<string> files;
        try
        {
            files = System.IO.Directory.EnumerateFiles(_directory, "*.lrc", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(path).Length > MaxFileSize)
                    continue;
                var header = await ReadHeaderAsync(path, ct).ConfigureAwait(false);
                if (!Matches(query, Path.GetFileNameWithoutExtension(path), header, out var title, out var artist, out var length))
                    continue;
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                return ToRecord(text, title, artist, length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or vanished file is skipped like a non-matching one.
            }
        }
        return null;
    }

    /// <summary>Title/artist from the tags, falling back to "Artist - Title" in the file name.</summary>
    internal static bool Matches(TrackQuery query, string fileName, string header,
        out string? title, out string? artist, out double? lengthSeconds)
    {
        title = artist = null;
        lengthSeconds = null;
        foreach (Match m in HeaderTag().Matches(header))
        {
            var value = m.Groups[2].Value;
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "ti": title ??= value; break;
                case "ar": artist ??= value; break;
                case "length": lengthSeconds ??= ParseLength(value); break;
            }
        }

        var dash = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            artist ??= fileName[..dash];
            title ??= fileName[(dash + 3)..];
        }

        return TrackMatcher.TitlesMatch(query.Title, title)
            && TrackMatcher.ArtistsMatch(query.Artist, artist)
            && TrackMatcher.DurationsMatch(query.Duration, lengthSeconds);
    }

    internal static LrclibRecord ToRecord(string text, string? title, string? artist, double? lengthSeconds)
    {
        if (LrcParser.Parse(text).Count > 0)
            return new LrclibRecord(0, title, artist, null, lengthSeconds, false, null, text);

        // No time tags: a plain lyric (metadata tag lines dropped).
        var plain = string.Join('\n', text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !LeadingTag().IsMatch(l.Trim())));
        return new LrclibRecord(0, title, artist, null, lengthSeconds, false, plain.Trim(), null);
    }

    private static double? ParseLength(string value)
    {
        // "3:45", "03:45.20" or plain seconds.
        var parts = value.Split(':');
        var ok = true;
        double total = 0;
        foreach (var part in parts)
        {
            ok &= double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n);
            total = total * 60 + n;
        }
        return ok && total > 0 ? total : null;
    }

    private static async Task<string> ReadHeaderAsync(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[HeaderChars];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
        return new string(buffer, 0, read).Replace("\r", string.Empty);
    }
}
