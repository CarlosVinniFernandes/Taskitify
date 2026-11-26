namespace Taskitify.Core.Lyrics;

/// <summary>
/// Projects the playback position locally between the (infrequent) updates reported by the
/// media session: position = lastReported + (now - reportedAt) while playing.
/// </summary>
public sealed class PlaybackClock
{
    // A report older than this while "playing" is treated as bogus rather than projected.
    private static readonly TimeSpan MaxProjection = TimeSpan.FromHours(1);

    private readonly Func<DateTimeOffset> _now;
    private TimeSpan _base;
    private DateTimeOffset _baseAt;

    public PlaybackClock(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _baseAt = _now();
    }

    public bool IsPlaying { get; private set; }

    public double Rate { get; private set; } = 1;

    public TimeSpan? Duration { get; set; }

    private TimeSpan Project(TimeSpan elapsed) => elapsed * Rate;

    public TimeSpan Position
    {
        get
        {
            var p = IsPlaying ? _base + Project(_now() - _baseAt) : _base;
            if (p < TimeSpan.Zero)
                return TimeSpan.Zero;
            if (Duration is { } d && d > TimeSpan.Zero && p > d)
                return d;
            return p;
        }
    }

    /// <summary>New track: jump to a position (usually zero) as of now.</summary>
    public void Reset(TimeSpan position = default)
    {
        _base = position;
        _baseAt = _now();
    }

    /// <summary>Play/pause without a fresh timeline report: freeze or resume from the projected position.</summary>
    public void SetPlaying(bool playing)
    {
        if (playing == IsPlaying)
            return;
        var p = Position;
        IsPlaying = playing;
        _base = p;
        _baseAt = _now();
    }

    public bool SetRate(double rate)
    {
        if (!double.IsFinite(rate) || rate is < 0.05 or > 16)
            rate = 1;
        if (rate == Rate)
            return false;
        Reset(Position);
        Rate = rate;
        return true;
    }

    /// <summary>Apply a position reported by the session, sampled at <paramref name="reportedAt"/>.</summary>
    public void Sync(TimeSpan reported, DateTimeOffset reportedAt, bool playing)
    {
        var now = _now();
        if (reportedAt > now || now - reportedAt > MaxProjection)
            reportedAt = now;

        IsPlaying = playing;
        _base = playing ? reported + Project(now - reportedAt) : reported;
        _baseAt = now;
    }
}
