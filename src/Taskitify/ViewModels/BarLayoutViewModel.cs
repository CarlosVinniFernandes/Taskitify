using System.Windows;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>
/// Backs one player picker in Settings (normal or minimized). Every element is a toggle
/// bound to the matching <see cref="BarLayout"/> flag; changes apply and save immediately.
/// </summary>
public sealed class BarLayoutViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly Func<AppSettings, BarLayout> _select;

    /// <param name="taskbar">A taskbar player, whose picker mirrors the progress placement (the mini player's is always below).</param>
    public BarLayoutViewModel(SettingsService settings, Func<AppSettings, BarLayout> select, string textLabel, string captionLabel, int minWidth, int maxWidth,
        bool taskbar = true)
    {
        _settings = settings;
        _select = select;
        _taskbar = taskbar;
        _textKey = textLabel;
        _captionKey = captionLabel;
        MinWidth = minWidth;
        MaxWidth = maxWidth;
        _settings.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    private readonly string _textKey;
    private readonly string _captionKey;
    private readonly bool _taskbar;
    public string TextLabel => LocalizationService.Get(_textKey);
    public string CaptionLabel => LocalizationService.Get(_captionKey);

    public int MinWidth { get; }

    public int MaxWidth { get; }

    public bool ShowCover { get => L.ShowCover; set => Update(l => l.ShowCover = value); }

    public bool ShowText { get => L.ShowText; set => Update(l => l.ShowText = value); }

    public bool ShowCaption { get => L.ShowCaption; set => Update(l => l.ShowCaption = value); }

    public bool ShowShuffle { get => L.ShowShuffle; set => Update(l => l.ShowShuffle = value); }

    public bool ShowPrevious { get => L.ShowPrevious; set => Update(l => l.ShowPrevious = value); }

    public bool ShowPlayPause { get => L.ShowPlayPause; set => Update(l => l.ShowPlayPause = value); }

    public bool ShowNext { get => L.ShowNext; set => Update(l => l.ShowNext = value); }

    public bool ShowRepeat { get => L.ShowRepeat; set => Update(l => l.ShowRepeat = value); }

    public bool ShowVolume { get => L.ShowVolume; set => Update(l => l.ShowVolume = value); }

    public bool ShowProgress { get => L.ShowProgress; set => Update(l => l.ShowProgress = value); }

    public int Width { get => L.Width; set => Update(l => l.Width = Math.Clamp(value, MinWidth, MaxWidth)); }

    /// <summary>The picker mirrors the progress placement: larger cover, line starting after it.</summary>
    private bool Beside => _taskbar && _settings.Current.ProgressPlacement == ProgressPlacement.BesideCover && L.ShowCover && L.ShowProgress;

    public double PickerCoverSize => Beside ? 36 : 30;

    public Thickness PickerProgressMargin => Beside ? new Thickness(54, 0, 10, 0) : new Thickness(10, 0, 10, 0);

    private BarLayout L => _select(_settings.Current);

    private void Update(Action<BarLayout> change) => _settings.Update(s => change(_select(s)));
}
