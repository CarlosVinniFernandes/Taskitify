using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>One of the floating player's hover buttons in Settings: its name, and whether it shows.</summary>
public sealed class PlayerToolItem : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    public PlayerToolItem(string id, Func<bool> get, Action<bool> set)
    {
        Id = id;
        _get = get;
        _set = set;
    }

    public string Id { get; }

    public string Label => LocalizationService.Get(Id switch
    {
        PlayerToolIds.Settings => "FloatingPlayerSettings",
        PlayerToolIds.Size => "PlayerToolSize",
        PlayerToolIds.Maximize => "MaximizeWindow",
        PlayerToolIds.FullScreen => "FullScreen",
        PlayerToolIds.Switch => "SwitchToMinimal",
        PlayerToolIds.ClickThrough => "MinimalClickThroughTip",
        PlayerToolIds.Lock => "MinimalLock",
        _ => "FloatingPlayerClose",
    });

    /// <summary>The icon (Segoe Fluent Icons), as on the player; the click-through and size buttons draw theirs.</summary>
    public string Glyph => Id switch
    {
        PlayerToolIds.Settings => "",
        PlayerToolIds.Maximize => "",
        PlayerToolIds.FullScreen => "",
        PlayerToolIds.Switch => "",
        PlayerToolIds.Lock => "",
        PlayerToolIds.Close => "",
        _ => string.Empty,
    };

    public bool IsShown
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
