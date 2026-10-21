using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

// Classes, not records: a record's hash code includes the PropertyChanged delegate, so it changes
// as soon as WPF listens to the item, and a ComboBox can then no longer select it.
public sealed class MonitorOption(string? deviceName, Func<string> getLabel) : System.ComponentModel.INotifyPropertyChanged
{
    public string? DeviceName { get; } = deviceName;
    public string Label => getLabel();
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
    // What screen readers announce for the item.
    public override string ToString() => Label;
}

public sealed class ChoiceOption<T>(T value, string key) : System.ComponentModel.INotifyPropertyChanged
{
    public T Value { get; } = value;
    public string Key { get; } = key;
    public string Label => LocalizationService.Get(Key);
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
    public override string ToString() => Label;
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly LyricsCache _cache;
    private readonly LyricsSyncService _sync;
    private readonly Action<bool> _setCalibrating;
    private bool _isCalibrating;
    private bool _startWithWindows;
    private bool _updatingStartup;

    public SettingsViewModel(SettingsService settings, LyricsCache cache, LyricsSyncService sync, bool isCalibrating, Action<bool> setCalibrating)
    {
        _settings = settings;
        Monitors = CreateMonitors();
        _cache = cache;
        _sync = sync;
        _isCalibrating = isCalibrating;
        _setCalibrating = setCalibrating;
        _startWithWindows = StartupService.IsEnabled;

        var (barMin, barMax) = BarLayout.WidthRange(minimized: false);
        var (minimizedMin, minimizedMax) = BarLayout.WidthRange(minimized: true);
        Bar = new BarLayoutViewModel(settings, x => x.Bar, "SongLyrics", "SongArtist", barMin, barMax);
        MinimizedBar = new BarLayoutViewModel(settings, x => x.MinimizedBar, "SongTitle", "Artist", minimizedMin, minimizedMax);
        MiniPlayer = new BarLayoutViewModel(settings, x => x.MiniPlayer, "SongTitle", "Artist", 310, 640, taskbar: false);
        ResetPlacementCommand = new RelayCommand(() => Update(s => s.ManualPlacement = false), () => S.ManualPlacement);
        ClearCacheCommand = new RelayCommand(() =>
        {
            try
            {
                _cache.Clear();
                _sync.Reload();
            }
            catch (IOException ex)
            {
                Log.Error("Could not clear cache", ex);
            }
        });
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.LocalDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LocalDataDirectory}\"") { UseShellExecute = true });
        });
        OpenLyricsFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.UserLyricsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.UserLyricsDirectory}\"") { UseShellExecute = true });
        });
    }

    /// <summary>The normal player layout.</summary>
    public BarLayoutViewModel Bar { get; }

    /// <summary>The compact layout shown while paused when minimizing is chosen.</summary>
    public BarLayoutViewModel MinimizedBar { get; }

    /// <summary>The popup with the lyrics.</summary>
    public BarLayoutViewModel MiniPlayer { get; }

    public bool IsMinimizeOnPause => S.PausedBehavior == PausedBehavior.Minimize;

    private IReadOnlyList<MonitorOption> CreateMonitors() => [new MonitorOption(null, () => LocalizationService.Get("PrimaryMonitor")),
            .. TaskbarPositionService.GetMonitors().Select((m, i) =>
                new MonitorOption(m.DeviceName, () => $"{LocalizationService.Get("Monitor")} {i + 1} — {m.Bounds.Width}×{m.Bounds.Height}{(m.IsPrimary ? LocalizationService.Get("PrimarySuffix") : "")}"))];

    public IReadOnlyList<MonitorOption> Monitors { get; }
    public IReadOnlyList<LanguageOption> Languages => LocalizationService.Languages;
    public string Language
    {
        get => LocalizationService.Language;
        set
        {
            if (string.IsNullOrEmpty(value) || value == Language) return;
            Update(s => s.Language = value);
            foreach (var option in NoLyricsOptions) option.Refresh();
            foreach (var option in BackgroundOptions) option.Refresh();
            foreach (var option in PrimaryTextOptions) option.Refresh();
            foreach (var option in ProgressPlacementOptions) option.Refresh();
            foreach (var option in PausedOptions) option.Refresh();
            foreach (var option in Monitors) option.Refresh();
        }
    }

    public IReadOnlyList<ChoiceOption<NoLyricsBehavior>> NoLyricsOptions { get; } =
    [
        new(NoLyricsBehavior.ShowTitle, "ShowTitle"),
        new(NoLyricsBehavior.HideOverlay, "HideWeather"),
    ];

    public IReadOnlyList<ChoiceOption<OverlayBackground>> BackgroundOptions { get; } =
    [
        new(OverlayBackground.AlbumGradient, "AlbumGradient"),
        new(OverlayBackground.Taskbar, "MatchTaskbar"),
    ];

    public IReadOnlyList<ChoiceOption<PrimaryText>> PrimaryTextOptions { get; } =
    [
        new(PrimaryText.Lyrics, "Lyrics"),
        new(PrimaryText.Track, "SongTitle"),
    ];

    public IReadOnlyList<ChoiceOption<ProgressPlacement>> ProgressPlacementOptions { get; } =
    [
        new(ProgressPlacement.Below, "ProgressBelow"),
        new(ProgressPlacement.BesideCover, "ProgressBeside"),
    ];

    public ProgressPlacement ProgressPlacement { get => S.ProgressPlacement; set => Update(s => s.ProgressPlacement = value); }

    public IReadOnlyList<ChoiceOption<PausedBehavior>> PausedOptions { get; } =
    [
        new(PausedBehavior.Hide, "HideWeather"),
        new(PausedBehavior.Minimize, "MinimizePlayer"),
    ];

    public PausedBehavior PausedBehavior { get => S.PausedBehavior; set => Update(s => s.PausedBehavior = value); }

    public ICommand ResetPlacementCommand { get; }

    public OverlayBackground Background { get => S.Background; set => Update(s => s.Background = value); }

    public PrimaryText PrimaryText { get => S.PrimaryText; set => Update(s => s.PrimaryText = value); }

    public bool MarqueeLongText { get => S.MarqueeLongText; set => Update(s => s.MarqueeLongText = value); }

    public ICommand ClearCacheCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    /// <summary>Folder for the user's own .lrc files, which take precedence over LRCLIB.</summary>
    public ICommand OpenLyricsFolderCommand { get; }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!_updatingStartup && Set(ref _startWithWindows, value))
                UpdateStartupAsync(value);
        }
    }

    private async void UpdateStartupAsync(bool enabled)
    {
        _updatingStartup = true;
        try
        {
            await StartupService.SetEnabledAsync(enabled);
            // Windows may refuse when the user or an administrator disabled the task.
            _startWithWindows = StartupService.IsEnabled;
            OnPropertyChanged(nameof(StartWithWindows));
        }
        finally { _updatingStartup = false; }
    }

    public bool EnableAnimations { get => S.EnableAnimations; set => Update(s => s.EnableAnimations = value); }

    public double FontSize { get => S.FontSize; set => Update(s => s.FontSize = Math.Clamp(Math.Round(value), 9, 18)); }

    public int SyncOffsetMs { get => S.SyncOffsetMs; set => Update(s => s.SyncOffsetMs = Math.Clamp(value, -5000, 5000)); }

    public NoLyricsBehavior NoLyricsBehavior { get => S.NoLyricsBehavior; set => Update(s => s.NoLyricsBehavior = value); }

    public string? MonitorDeviceName { get => S.MonitorDeviceName; set => Update(s => s.MonitorDeviceName = value); }

    public bool ShowTrayIcon { get => S.ShowTrayIcon; set => Update(s => s.ShowTrayIcon = value); }

    public bool DebugMode { get => S.DebugMode; set => Update(s => s.DebugMode = value); }

    public bool IsManualPlacement => S.ManualPlacement;

    public bool IsCalibrating
    {
        get => _isCalibrating;
        set
        {
            if (Set(ref _isCalibrating, value))
                _setCalibrating(value);
        }
    }

    public void NudgeOffset(int deltaMs) => SyncOffsetMs += deltaMs;

    /// <summary>Calibration may end from the overlay itself (Esc or the context menu).</summary>
    public void SyncCalibrating(bool value)
    {
        _isCalibrating = value;
        OnPropertyChanged(nameof(IsCalibrating));
        OnPropertyChanged(nameof(IsManualPlacement));
    }

    private AppSettings S => _settings.Current;

    private void Update(Action<AppSettings> change)
    {
        _settings.Update(change);
        OnPropertyChanged(string.Empty); // refresh every binding; the window is tiny
    }
}
