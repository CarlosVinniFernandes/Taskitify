using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Taskitify.Core.Lyrics;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

public sealed record MonitorOption(string? DeviceName, string Label);

public sealed record ChoiceOption<T>(T Value, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly LyricsCache _cache;
    private readonly LyricsSyncService _sync;
    private readonly Action<bool> _setCalibrating;
    private bool _isCalibrating;
    private bool _startWithWindows;

    public SettingsViewModel(SettingsService settings, LyricsCache cache, LyricsSyncService sync, bool isCalibrating, Action<bool> setCalibrating)
    {
        _settings = settings;
        _cache = cache;
        _sync = sync;
        _isCalibrating = isCalibrating;
        _setCalibrating = setCalibrating;
        _startWithWindows = StartupService.IsEnabled;

        Bar = new BarLayoutViewModel(settings, x => x.Bar, "Letra da música", "Música · Artista", 100, 900);
        MinimizedBar = new BarLayoutViewModel(settings, x => x.MinimizedBar, "Nome da música", "Artista", 60, 600);

        Monitors = [new MonitorOption(null, "Monitor principal"),
            .. TaskbarPositionService.GetMonitors().Select((m, i) =>
                new MonitorOption(m.DeviceName, $"Monitor {i + 1} — {m.Bounds.Width}×{m.Bounds.Height}{(m.IsPrimary ? " (principal)" : "")}"))];

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
    }

    /// <summary>"Monte a sua barra": the normal player.</summary>
    public BarLayoutViewModel Bar { get; }

    /// <summary>"Estilo do player minimizado": shown while paused when minimizing is chosen.</summary>
    public BarLayoutViewModel MinimizedBar { get; }

    public bool IsMinimizeOnPause => S.PausedBehavior == PausedBehavior.Minimize;

    public IReadOnlyList<MonitorOption> Monitors { get; }

    public IReadOnlyList<ChoiceOption<NoLyricsBehavior>> NoLyricsOptions { get; } =
    [
        new(NoLyricsBehavior.ShowTitle, "Mostrar o nome da música"),
        new(NoLyricsBehavior.HideOverlay, "Esconder (mostrar o clima)"),
    ];

    public IReadOnlyList<ChoiceOption<OverlayBackground>> BackgroundOptions { get; } =
    [
        new(OverlayBackground.AlbumGradient, "Degradê da capa do álbum"),
        new(OverlayBackground.Taskbar, "Igual à barra de tarefas"),
    ];

    public IReadOnlyList<ChoiceOption<PrimaryText>> PrimaryTextOptions { get; } =
    [
        new(PrimaryText.Lyrics, "Letra"),
        new(PrimaryText.Track, "Nome da música"),
    ];

    public IReadOnlyList<ChoiceOption<ProgressPlacement>> ProgressPlacementOptions { get; } =
    [
        new(ProgressPlacement.Below, "Abaixo de tudo (capa em cima da barra)"),
        new(ProgressPlacement.BesideCover, "À direita da capa (capa maior)"),
    ];

    public ProgressPlacement ProgressPlacement { get => S.ProgressPlacement; set => Update(s => s.ProgressPlacement = value); }

    public IReadOnlyList<ChoiceOption<PausedBehavior>> PausedOptions { get; } =
    [
        new(PausedBehavior.Hide, "Esconder (mostrar o clima)"),
        new(PausedBehavior.Minimize, "Minimizar o player"),
    ];

    public PausedBehavior PausedBehavior { get => S.PausedBehavior; set => Update(s => s.PausedBehavior = value); }

    public ICommand ResetPlacementCommand { get; }

    public OverlayBackground Background { get => S.Background; set => Update(s => s.Background = value); }

    public PrimaryText PrimaryText { get => S.PrimaryText; set => Update(s => s.PrimaryText = value); }

    public bool MarqueeLongText { get => S.MarqueeLongText; set => Update(s => s.MarqueeLongText = value); }

    public ICommand ClearCacheCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (Set(ref _startWithWindows, value))
                StartupService.SetEnabled(value);
        }
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
