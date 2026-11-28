namespace Taskitify.Core.Media;

/// <summary>
/// Decides whether the player is really playing when the media session lags. Spotify publishes its
/// play/pause state to Windows about 3 s late, and sometimes never (measured: audio stopped 0.4 s
/// after a pause and resumed 0.03 s after a play, while the session's status changed 3 s later or
/// not at all). Two quicker signals win over the report:
/// <list type="bullet">
/// <item>a play/pause sent from this app: its target, for a short window;</item>
/// <item>the player's audio level: sound means playing; silence that follows sound, while the
/// report still says playing, means paused.</item>
/// </list>
/// <see cref="PlayingOverride"/> is null when the report should be trusted as it is.
/// </summary>
public sealed class PlaybackStateArbiter
{
    /// <summary>Peak level (0..1) above which the player counts as making sound (about -66 dBFS).</summary>
    public const float SoundThreshold = 0.0005f;

    /// <summary>Silence that lasts this long after sound counts as a pause (Spotify's pause fade is ~0.4 s).</summary>
    public static readonly TimeSpan SilenceForPause = TimeSpan.FromMilliseconds(800);

    /// <summary>How long a play/pause sent from the app wins over the other signals.</summary>
    public static readonly TimeSpan CommandWindow = TimeSpan.FromSeconds(3);

    /// <summary>Without an audio level, how long a sent play/pause waits for the report to agree.</summary>
    public static readonly TimeSpan CommandWindowWithoutAudio = TimeSpan.FromSeconds(5);

    private readonly Func<DateTimeOffset> _now;
    private bool _reportedPlaying;
    private bool _hasAudio;
    private bool _sounding;
    private bool _heardSound;
    private DateTimeOffset _soundSince;
    private DateTimeOffset? _silentSince;
    private (bool Playing, DateTimeOffset At)? _command;

    public PlaybackStateArbiter(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Whether the player is playing, when that differs from what the report alone would say; null to follow the report.</summary>
    public bool? PlayingOverride => Resolve().Playing;

    /// <summary>When the overriding state began (the command, the first sound, or the start of the silence).</summary>
    public DateTimeOffset? OverrideSince => Resolve().Since;

    /// <summary>The state the media session reports.</summary>
    public void Reported(bool playing) => _reportedPlaying = playing;

    /// <summary>The app just asked the player to play or pause.</summary>
    public void Commanded(bool playing) => _command = (playing, _now());

    /// <summary>The player refused the command: forget it.</summary>
    public void CommandFailed() => _command = null;

    /// <summary>A peak level sample of the player's audio, or null when its audio session is not found.</summary>
    public void Audio(float? peak)
    {
        var now = _now();
        if (peak is not { } level)
        {
            _hasAudio = false;
            _sounding = false;
            _silentSince = null;
            return;
        }

        _hasAudio = true;
        if (level > SoundThreshold)
        {
            if (!_sounding)
                _soundSince = now;
            _sounding = true;
            _heardSound = true;
            _silentSince = null;
        }
        else
        {
            _sounding = false;
            _silentSince ??= now;
        }
    }

    /// <summary>
    /// The position kept advancing while silence said paused (the player is muted, or the audio
    /// moved to another device): stop reading silence as a pause until sound is heard again.
    /// </summary>
    public void SilenceDisproved() => _heardSound = false;

    /// <summary>No player session: forget everything.</summary>
    public void Reset()
    {
        _reportedPlaying = _hasAudio = _sounding = _heardSound = false;
        _silentSince = null;
        _command = null;
    }

    private (bool? Playing, DateTimeOffset? Since) Resolve()
    {
        var now = _now();
        if (_command is { } command)
        {
            var window = _hasAudio ? CommandWindow : CommandWindowWithoutAudio;
            if (now - command.At >= window || (!_hasAudio && _reportedPlaying == command.Playing))
                _command = null;
            else if (_hasAudio && command.Playing && _sounding)
                _command = null; // the sound confirms it
            else
                return (command.Playing, command.At);
        }

        if (!_hasAudio)
            return (null, null);
        if (_sounding)
            return (true, _soundSince);
        if (_reportedPlaying && _heardSound && _silentSince is { } silent && now - silent >= SilenceForPause)
            return (false, silent);
        return (null, null);
    }
}
