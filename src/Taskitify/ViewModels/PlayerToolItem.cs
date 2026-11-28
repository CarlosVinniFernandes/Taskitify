using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>A button in a player toolbar preview.</summary>
public sealed class PlayerToolItem : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private readonly string _settingsLabel;

    public PlayerToolItem(string id, Func<bool> get, Action<bool> set, string settingsLabel = "FloatingPlayerSettings")
    {
        Id = id;
        _get = get;
        _set = set;
        _settingsLabel = settingsLabel;
    }

    public string Id { get; }

    public string Label => LocalizationService.Get(Id switch
    {
        PlayerToolIds.Settings => _settingsLabel,
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
