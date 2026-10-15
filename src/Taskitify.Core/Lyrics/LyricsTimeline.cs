namespace Taskitify.Core.Lyrics;

/// <summary>Sorted synced lines with O(log n) lookup of the line active at a position.</summary>
public sealed class LyricsTimeline
{
    private readonly LyricsLine[] _lines;

    public LyricsTimeline(IEnumerable<LyricsLine> lines)
    {
        // OrderBy is stable, so lines sharing a timestamp keep their file order.
        _lines = lines.OrderBy(l => l.Time).ToArray();
    }

    public IReadOnlyList<LyricsLine> Lines => _lines;

    public int Count => _lines.Length;

    /// <summary>Index of the last line whose time is &lt;= position, or -1 before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        int lo = 0, hi = _lines.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (_lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }

    /// <summary>Start time of the next line after the one active at position, or null at the end.</summary>
    public TimeSpan? NextChangeAfter(TimeSpan position)
    {
        var next = IndexAt(position) + 1;
        return next < _lines.Length ? _lines[next].Time : null;
    }
}
