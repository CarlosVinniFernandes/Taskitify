using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>One karaoke source in Settings: what it is, what it can do, and whether it is used.</summary>
public sealed class LyricsSourceItem : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    public LyricsSourceItem(string id, Func<bool> get, Action<bool> set)
    {
        Id = id;
        _get = get;
        _set = set;
    }

    public string Id { get; }

    public string Name => Id switch
    {
        KaraokeSourceIds.LrcRed => "lrc.red",
        KaraokeSourceIds.Lrclib => "LRCLIB",
        _ => Id,
    };

    public string Description => LocalizationService.Get(Id switch
    {
        KaraokeSourceIds.LrcRed => "SourceLrcRedHint",
        _ => "SourceLrclibHint",
    });

    /// <summary>What its lyrics can have, as short tags (measured: see docs/lyrics-sources.md).</summary>
    public IReadOnlyList<string> Capabilities => (Id switch
    {
        KaraokeSourceIds.LrcRed => new[] { "CapSyllable", "CapWord", "CapBacking", "CapDuet" },
        _ => new[] { "CapWord", "CapLine" },
    }).Select(LocalizationService.Get).ToList();

    public bool IsEnabled
    {
        get => _get();
        set
        {
            if (value == _get())
                return;
            _set(value);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
