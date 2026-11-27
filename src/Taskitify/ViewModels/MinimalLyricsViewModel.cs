using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>
/// Minimal Mode: the lyrics alone, floating over the desktop. It follows the same lyrics, clock
/// and Lyric Flow as the taskbar and the mini player (<see cref="LyricsSyncService"/>), with no
/// timer of its own, and listens to nothing while its window is hidden.
/// </summary>
public sealed class MinimalLyricsViewModel : ObservableObject, IDisposable
{
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _sync;
    private readonly SettingsService _settings;
    private bool _isActive;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private bool _isPlaying;
    private string? _statusMessage;
    private int _currentIndex = -1;

    public MinimalLyricsViewModel(ISpotifyMonitor monitor, LyricsSyncService sync, SettingsService settings)
    {
        _monitor = monitor;
        _sync = sync;
        _settings = settings;
        PlayPauseCommand = new RelayCommand(() => _ = _monitor.PlayPauseAsync(), () => _monitor.HasSession);
        NextCommand = new RelayCommand(() => _ = _monitor.NextAsync(), () => _monitor.CanGoNext);
        PreviousCommand = new RelayCommand(() => _ = _monitor.PreviousAsync(), () => _monitor.CanGoPrevious);
        ToggleLockCommand = new RelayCommand(() => _settings.Update(s => s.Minimal.Locked = !s.Minimal.Locked));
        ClickThroughCommand = new RelayCommand(() => _settings.Update(s => s.Minimal.ClickThrough = true));
        CloseCommand = new RelayCommand(() => _settings.Update(s => s.Minimal.Enabled = false));
        SettingsCommand = new RelayCommand(() => (Application.Current as App)?.ShowSettings(Views.SettingsPage.Minimal));
        MiniPlayerCommand = new RelayCommand(() => MiniPlayerRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<LyricLineViewModel> Lines { get; } = [];

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ToggleLockCommand { get; }

    /// <summary>The toolbar's "let clicks through" button (Ctrl + Alt + click toggles it too).</summary>
    public ICommand ClickThroughCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand MiniPlayerCommand { get; }

    /// <summary>The toolbar's player button: the mini player opens beside the lyrics.</summary>
    public event EventHandler? MiniPlayerRequested;

    public MinimalModeSettings S => _settings.Current.Minimal;

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public string SongLine => string.IsNullOrEmpty(Artist) ? Title : $"{Title}  ·  {Artist}";
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public string PlayGlyph => IsPlaying ? "" : "";

    /// <summary>Shown instead of the lyrics when there are no synced ones (loading, instrumental, none...).</summary>
    public string? StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public int CurrentIndex { get => _currentIndex; private set => Set(ref _currentIndex, value); }

    public bool IsLocked => S.Locked;
    public string LockGlyph => S.Locked ? "" : "";
    public string LockTip => LocalizationService.Get(S.Locked ? "MinimalUnlock" : "MinimalLock");

    // ---- Look (Settings > Minimal Mode); type, highlight and motion are independent of each other ----

    public LyricFlowMode FlowMode => _settings.Current.TaskbarLyricFlow;
    public LyricMotionMode MotionMode => S.Motion;
    public bool ExpressiveMotion => MotionMode == LyricMotionMode.Expressive;

    /// <summary>"No motion" keeps the composition and only stops animating it (as in the mini player).</summary>
    private LyricMotionMode LayoutMotion => MotionMode == LyricMotionMode.Off ? LyricMotionMode.Smooth : MotionMode;

    private bool DisplayType => S.Typeface != LyricsTypeface.System;
    public FontFamily LyricsFont => MiniPlayerViewModel.FontFor(S.Typeface);
    private double? _fitSize;

    /// <summary>The size used: the chosen one, or smaller when the window is too small for the verses (auto fit).</summary>
    public double LyricsFontSize => S.AutoFit && _fitSize is { } fit ? Math.Min(fit, MinimalModeSettings.ClampFontSize(S.FontSize)) : MinimalModeSettings.ClampFontSize(S.FontSize);

    /// <summary>The view reports the size that fits its lyrics area (null: no limit).</summary>
    public void SetFitSize(double? size)
    {
        if (_fitSize == size || (_fitSize is { } a && size is { } b && Math.Abs(a - b) < 0.5))
            return;
        _fitSize = size;
        OnPropertyChanged(nameof(LyricsFontSize));
        OnPropertyChanged(nameof(LineHeight));
        OnPropertyChanged(nameof(LinePadding));
        OnPropertyChanged(nameof(SongFontSize));
    }

    public double LineHeight => LyricsFontSize * 1.14;
    public Thickness LinePadding => new(6, LyricsFontSize * .2 * MinimalModeSettings.ClampSpacing(S.LineSpacing), 6, LyricsFontSize * .2 * MinimalModeSettings.ClampSpacing(S.LineSpacing));
    public TextAlignment LyricsTextAlignment => S.Alignment == LyricsAlignment.Left ? TextAlignment.Left : TextAlignment.Center;
    public HorizontalAlignment SongAlignment => S.Alignment == LyricsAlignment.Left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    public Point ScaleOrigin => S.Alignment == LyricsAlignment.Left ? new(0, .5) : new(.5, .5);
    public double SongFontSize => Math.Max(12, LyricsFontSize * .44);
    public bool ShowSong => S.ShowSong;
    public bool ShowMiniPlayerButton => S.ShowMiniPlayerButton;
    public bool ShowSettingsButton => S.ShowSettingsButton;
    public bool ShowClickThroughButton => S.ShowClickThroughButton;
    public bool ShowLockButton => S.ShowLockButton;
    public bool ShowCloseButton => S.ShowCloseButton;
    public bool ShowControls => S.ShowControls;
    public double ScrollMilliseconds { get; private set; }
    public LyricFill CurrentFill => _sync.Fill(CurrentIndex, FlowMode);

    /// <summary>The clock moved (seek, pause, resume): the Lyric Flow ink follows.</summary>
    public event EventHandler? LyricPositionChanged;

    /// <summary>Raised after the current verse changed, so the view can bring it to its place.</summary>
    public event EventHandler? CurrentLineChanged;

    /// <summary>The look changed: the view re-applies colours, background and size.</summary>
    public event EventHandler? LookChanged;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!Set(ref _isActive, value))
                return;
            if (value)
            {
                LocalizationService.Changed += OnTrackChanged;
                _monitor.TrackChanged += OnTrackChanged;
                _monitor.PlaybackChanged += OnPlaybackChanged;
                _monitor.TimelineChanged += OnTimelineChanged;
                _sync.LyricsChanged += OnLyricsChanged;
                _sync.CurrentLineChanged += OnCurrentLineChanged;
                _settings.Changed += OnSettingsChanged;
                RefreshAll();
                OnSettingsChanged(this, EventArgs.Empty);
            }
            else
            {
                Unsubscribe();
                Lines.Clear();
            }
        }
    }

    public void Dispose() => Unsubscribe();

    private void Unsubscribe()
    {
        LocalizationService.Changed -= OnTrackChanged;
        _monitor.TrackChanged -= OnTrackChanged;
        _monitor.PlaybackChanged -= OnPlaybackChanged;
        _monitor.TimelineChanged -= OnTimelineChanged;
        _sync.LyricsChanged -= OnLyricsChanged;
        _sync.CurrentLineChanged -= OnCurrentLineChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnTrackChanged(object? sender, EventArgs e) => RefreshAll();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
        UpdateCurrentLine();
        LookChanged?.Invoke(this, EventArgs.Empty);
        LyricPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        IsPlaying = _monitor.IsPlaying;
        OnPropertyChanged(nameof(PlayGlyph));
        CommandManager.InvalidateRequerySuggested();
        if (!IsPlaying)
            UpdateCurrentLine();
        LyricPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimelineChanged(object? sender, EventArgs e) => LyricPositionChanged?.Invoke(this, EventArgs.Empty);

    private void OnLyricsChanged(object? sender, EventArgs e) => RebuildLines();

    private void OnCurrentLineChanged(object? sender, EventArgs e) => UpdateCurrentLine(_sync.ChangedAtLineBoundary);

    private void RefreshAll()
    {
        var track = _monitor.Track;
        Title = track?.Title ?? LocalizationService.Get("NothingPlaying");
        Artist = track?.Artist ?? string.Empty;
        OnPropertyChanged(nameof(SongLine));
        IsPlaying = _monitor.IsPlaying;
        OnPropertyChanged(nameof(PlayGlyph));
        RebuildLines();
    }

    private void RebuildLines()
    {
        Lines.Clear();
        _currentIndex = -1;
        if (_sync.Timeline is { } timeline)
        {
            for (var i = 0; i < timeline.Count; i++)
                Lines.Add(new LyricLineViewModel(i, timeline.Lines[i].Time, timeline.Lines[i].Text));
        }
        // Nothing at all while nothing plays or the lyrics load (over a game, silence is the point);
        // without synced lyrics, the song itself, quietly.
        StatusMessage = _monitor.Track is null ? null : _sync.State switch
        {
            LyricsState.Synced or LyricsState.Loading or LyricsState.None => null,
            LyricsState.Instrumental => "♪  " + SongLine,
            _ => SongLine,
        };
        UpdateCurrentLine();
    }

    /// <summary>
    /// The verses' emphasis, as in the mini player (same highlight and motion rules), and only
    /// the chosen number of them around the current one: the others fade out.
    /// </summary>
    private void UpdateCurrentLine(bool natural = false)
    {
        var index = _sync.CurrentIndex;
        var mode = MotionMode;
        var maximum = mode == LyricMotionMode.Expressive ? 410 : 330;
        var remaining = (_sync.NextChangeAfterCurrent - _sync.LyricsPosition)?.TotalMilliseconds / _sync.PlaybackRate;
        ScrollMilliseconds = natural && index != CurrentIndex && _settings.Current.EnableAnimations && SystemParameters.ClientAreaAnimation
            ? mode == LyricMotionMode.Off ? 320 : Math.Clamp((remaining ?? maximum * 1.35) * .74, 32, maximum)
            : 0;
        var (before, after) = S.Around;
        var anchor = Math.Max(index, 0);
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            var distance = i - anchor;
            line.MotionMilliseconds = mode != LyricMotionMode.Off && Math.Abs(distance) <= Math.Max(before, after) + 1 ? ScrollMilliseconds : 0;
            line.IsCurrent = i == index;
            line.IsPast = i < index;
            line.Emphasize(S.Highlight, distance, started: index >= 0, heavy: DisplayType, motion: LayoutMotion);
            // Smaller verses away from the current one: Minimal Mode's own composition, the same with
            // motion on or off (motion only animates between sizes).
            if (S.GraduatedSizes)
                line.Scale = S.SizeAt(index < 0 ? distance + 1 : distance) * (distance == 0 && index >= 0 ? Math.Max(1, line.Scale) : 1);
            // Before the first verse, the first ones wait below the middle.
            if (index < 0 ? distance > before + after : distance < -before || distance > after)
                line.Emphasis = 0;
        }
        CurrentIndex = index;
        CurrentLineChanged?.Invoke(this, EventArgs.Empty);
    }
}
