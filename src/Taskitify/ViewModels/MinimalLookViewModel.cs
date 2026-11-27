using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

/// <summary>
/// Settings > Minimal Mode: the same kind of pictured choices as the mini player's (highlight,
/// motion, alignment, type), plus its own (verses shown, background, text colour, legibility).
/// Every change applies at once and shows on the real floating lyrics.
/// </summary>
public sealed class MinimalLookViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ISpotifyMonitor _monitor;
    private readonly ThemeService _theme;
    private DispatcherOperation? _refresh;
    private bool _active;
    private Color? _diagramColor;
    private object? _sceneKey;

    public MinimalLookViewModel(SettingsService settings, ISpotifyMonitor monitor, ThemeService theme)
    {
        _settings = settings;
        _monitor = monitor;
        _theme = theme;
        LinesOptions = Options<MinimalLines>("MinimalLines");
        Highlights = Options<LyricsHighlight>("LookHighlight");
        Motions = Options<LyricMotionMode>("Motion");
        Alignments = Options<LyricsAlignment>("LookAlign");
        Typefaces = Options<LyricsTypeface>("LookTypeface");
        Backdrops = Options<MinimalBackdrop>("MinimalBackdrop");
        TextColors = Options<MinimalTextColor>("MinimalText");
        Legibilities = Options<MinimalLegibility>("MinimalLegibility");
        ResetPlacementCommand = new RelayCommand(() => ResetPlacementRequested?.Invoke(this, EventArgs.Empty));
        PausedOptions = Options<MinimalPaused>("MinimalPaused");
        PlaceCommand = new RelayCommand(p =>
        {
            if (Enum.TryParse<Core.Layout.FloatingSpot>(p as string, out var spot))
            {
                PlaceRequested?.Invoke(this, spot);
                PreviewRequested?.Invoke(this, EventArgs.Empty);
            }
        });
        CaptureHotkeyCommand = new RelayCommand(() => IsCapturing = !IsCapturing);
        RemoveAppCommand = new RelayCommand(p =>
        {
            if (p is string name)
            {
                AutoApps.Remove(name);
                Change(s => s.AutoApps.RemoveAll(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)), preview: false);
            }
        });
        foreach (var app in _settings.Current.Minimal.AutoApps)
            AutoApps.Add(app);
    }

    private static LookOption<T>[] Options<T>(string key) where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new LookOption<T>(value, LocalizationService.Get(key + value))).ToArray();

    private MinimalModeSettings S => _settings.Current.Minimal;

    /// <summary>A look changed while Minimal Mode is off: the app shows the floating lyrics until the settings close.</summary>
    public event EventHandler? PreviewRequested;

    public event EventHandler? ResetPlacementRequested;

    public ICommand ResetPlacementCommand { get; }

    private void Change(Action<MinimalModeSettings> change, bool preview = true)
    {
        _settings.Update(s => change(s.Minimal));
        OnPropertyChanged(string.Empty);
        if (preview)
            PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- On, and how it lives with the other players ----

    public bool Enabled { get => S.Enabled; set => Change(s => s.Enabled = value, preview: false); }
    public bool KeepTaskbar { get => S.KeepTaskbar; set => Change(s => s.KeepTaskbar = value, preview: false); }
    public bool Topmost { get => S.Topmost; set => Change(s => s.Topmost = value); }
    public bool Locked { get => S.Locked; set => Change(s => s.Locked = value); }
    public bool ClickThrough { get => S.ClickThrough; set => Change(s => s.ClickThrough = value); }
    public bool ShowSong { get => S.ShowSong; set => Change(s => s.ShowSong = value); }
    public bool ShowControls { get => S.ShowControls; set => Change(s => s.ShowControls = value); }
    public bool GraduatedSizes { get => S.GraduatedSizes; set => Change(s => s.GraduatedSizes = value); }

    /// <summary>The floating lyrics (Minimal Mode as such); turning them on leaves the floating player.</summary>
    public bool LyricsOn
    {
        get => S.Enabled && !S.NotThatMinimal;
        set => Change(s => (s.Enabled, s.NotThatMinimal) = (value, false), preview: false);
    }

    /// <summary>"Not that minimal": the whole mini player floats instead of the lyrics alone (one or the other).</summary>
    public bool FloatingPlayerOn
    {
        get => S.Enabled && S.NotThatMinimal;
        set => Change(s => (s.Enabled, s.NotThatMinimal) = (value, value), preview: false);
    }

    // ---- While paused ----

    public IReadOnlyList<LookOption<MinimalPaused>> PausedOptions { get; }

    public LookOption<MinimalPaused>? WhenPaused
    {
        get => PausedOptions.FirstOrDefault(o => o.Value == S.WhenPaused);
        set { if (value is not null && value.Value != S.WhenPaused) Change(s => s.WhenPaused = value.Value, preview: false); }
    }

    // ---- One-click places ----

    public ICommand PlaceCommand { get; }

    public event EventHandler<Core.Layout.FloatingSpot>? PlaceRequested;

    // ---- The shortcut that turns it on and off (works in games) ----

    public bool HotkeyEnabled { get => S.HotkeyEnabled; set => Change(s => s.HotkeyEnabled = value, preview: false); }

    /// <summary>Set by the app: the combination is taken by another app.</summary>
    public Func<bool>? HotkeyTaken { get; set; }

    public bool HotkeyFailed => S.HotkeyEnabled && !IsCapturing && HotkeyTaken?.Invoke() == true;

    private bool _capturing;

    /// <summary>Waiting for the new combination (the next keys pressed in Settings).</summary>
    public bool IsCapturing
    {
        get => _capturing;
        set
        {
            if (_capturing == value)
                return;
            _capturing = value;
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(HotkeyText));
            OnPropertyChanged(nameof(HotkeyFailed));
            CapturingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The app stops listening to the shortcut while a new one is typed.</summary>
    public event EventHandler? CapturingChanged;

    public ICommand CaptureHotkeyCommand { get; }

    public string HotkeyText => IsCapturing ? LocalizationService.Get("MinimalHotkeyPress") : Describe(S.HotkeyModifiers, S.HotkeyKey);

    public static string Describe(int modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add(LocalizationService.Get("KeyCtrl"));
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        var name = KeyInterop.KeyFromVirtualKey(key).ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            name = name[1..];
        parts.Add(name);
        return string.Join(" + ", parts);
    }

    /// <summary>A key pressed while capturing: a combination with Ctrl, Alt or Win becomes the shortcut; Esc cancels.</summary>
    public bool Capture(Key key, ModifierKeys modifiers)
    {
        if (!IsCapturing)
            return false;
        if (key == Key.Escape)
        {
            IsCapturing = false;
            return true;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return true;
        var mods = (modifiers.HasFlag(ModifierKeys.Alt) ? 1 : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2 : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8 : 0);
        if ((mods & (1 | 2 | 8)) == 0)
            return true; // a letter alone would be typed into games: wait for a real combination
        _capturing = false;
        Change(s => (s.HotkeyModifiers, s.HotkeyKey, s.HotkeyEnabled) = (mods, KeyInterop.VirtualKeyFromKey(key), true), preview: false);
        OnPropertyChanged(nameof(IsCapturing));
        CapturingChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ---- Apps that bring it up on their own ----

    public System.Collections.ObjectModel.ObservableCollection<string> AutoApps { get; } = [];

    public ICommand RemoveAppCommand { get; }

    private IReadOnlyList<string> _runningApps = [];

    /// <summary>Apps with a window open now (read when the list opens, not before).</summary>
    public IReadOnlyList<string> RunningApps { get => _runningApps; private set { _runningApps = value; OnPropertyChanged(nameof(RunningApps)); } }

    public void RefreshRunningApps()
    {
        var self = Environment.ProcessId;
        var names = new List<string>();
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.Id != self && process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    names.Add(process.ProcessName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        RunningApps = names.Where(n => !AutoApps.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Picking an app in the list adds it at once (one click).</summary>
    public string? AppToAdd
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || AutoApps.Contains(value, StringComparer.OrdinalIgnoreCase))
                return;
            AutoApps.Add(value);
            Change(s => s.AutoApps.Add(value), preview: false);
            OnPropertyChanged(nameof(AppToAdd));
        }
    }

    public bool HasAutoApps => AutoApps.Count > 0;
    public bool AutoFit { get => S.AutoFit; set => Change(s => s.AutoFit = value); }
    public bool ShowMiniPlayerButton { get => S.ShowMiniPlayerButton; set => Change(s => s.ShowMiniPlayerButton = value); }
    public bool ShowSettingsButton { get => S.ShowSettingsButton; set => Change(s => s.ShowSettingsButton = value); }
    public bool ShowClickThroughButton { get => S.ShowClickThroughButton; set => Change(s => s.ShowClickThroughButton = value); }
    public bool ShowLockButton { get => S.ShowLockButton; set => Change(s => s.ShowLockButton = value); }
    public bool ShowCloseButton { get => S.ShowCloseButton; set => Change(s => s.ShowCloseButton = value); }
    public bool ShowClickThroughMark { get => S.ShowClickThroughMark; set => Change(s => s.ShowClickThroughMark = value); }
    public bool ControlsOnHover { get => S.ControlsOnHover; set => Change(s => s.ControlsOnHover = value); }

    // ---- Lyrics ----

    public IReadOnlyList<LookOption<MinimalLines>> LinesOptions { get; }
    public IReadOnlyList<LookOption<LyricsHighlight>> Highlights { get; }
    public IReadOnlyList<LookOption<LyricMotionMode>> Motions { get; }
    public IReadOnlyList<LookOption<LyricsAlignment>> Alignments { get; }
    public IReadOnlyList<LookOption<LyricsTypeface>> Typefaces { get; }

    public LookOption<MinimalLines>? Lines
    {
        get => LinesOptions.FirstOrDefault(o => o.Value == S.Lines);
        set { if (value is not null && value.Value != S.Lines) Change(s => s.Lines = value.Value); }
    }

    public LookOption<LyricsHighlight>? Highlight
    {
        get => Highlights.FirstOrDefault(o => o.Value == S.Highlight);
        set { if (value is not null && value.Value != S.Highlight) Change(s => s.Highlight = value.Value); }
    }

    public LookOption<LyricMotionMode>? Motion
    {
        get => Motions.FirstOrDefault(o => o.Value == S.Motion);
        set { if (value is not null && value.Value != S.Motion) Change(s => s.Motion = value.Value); }
    }

    public LookOption<LyricsAlignment>? Alignment
    {
        get => Alignments.FirstOrDefault(o => o.Value == S.Alignment);
        set { if (value is not null && value.Value != S.Alignment) Change(s => s.Alignment = value.Value); }
    }

    public LookOption<LyricsTypeface>? Typeface
    {
        get => Typefaces.FirstOrDefault(o => o.Value == S.Typeface);
        set { if (value is not null && value.Value != S.Typeface) Change(s => s.Typeface = value.Value); }
    }

    public double FontSize
    {
        get => MinimalModeSettings.ClampFontSize(S.FontSize);
        set { if (MinimalModeSettings.ClampFontSize(value) != S.FontSize) Change(s => s.FontSize = MinimalModeSettings.ClampFontSize(value)); }
    }

    /// <summary>The room between verses, in percent of the usual.</summary>
    public double SpacingPercent
    {
        get => Math.Round(MinimalModeSettings.ClampSpacing(S.LineSpacing) * 100);
        set { var v = MinimalModeSettings.ClampSpacing(value / 100); if (v != S.LineSpacing) Change(s => s.LineSpacing = v); }
    }

    // ---- Background and text ----

    public IReadOnlyList<LookOption<MinimalBackdrop>> Backdrops { get; }
    public IReadOnlyList<LookOption<MinimalTextColor>> TextColors { get; }
    public IReadOnlyList<LookOption<MinimalLegibility>> Legibilities { get; }

    public LookOption<MinimalBackdrop>? Backdrop
    {
        get => Backdrops.FirstOrDefault(o => o.Value == S.Backdrop);
        set { if (value is not null && value.Value != S.Backdrop) Change(s => s.Backdrop = value.Value); }
    }

    public LookOption<MinimalTextColor>? TextColor
    {
        get => TextColors.FirstOrDefault(o => o.Value == S.TextColor);
        set { if (value is not null && value.Value != S.TextColor) Change(s => s.TextColor = value.Value); }
    }

    public LookOption<MinimalLegibility>? Legibility
    {
        get => Legibilities.FirstOrDefault(o => o.Value == S.Legibility);
        set { if (value is not null && value.Value != S.Legibility) Change(s => s.Legibility = value.Value); }
    }

    /// <summary>A clear background has no opacity to set: the slider says so instead of doing nothing.</summary>
    public bool HasBackdrop => S.Backdrop != MinimalBackdrop.Clear;
    public bool NoBackdrop => !HasBackdrop;

    public double OpacityPercent
    {
        get => Math.Round(Math.Clamp(S.BackdropOpacity, 0, 1) * 100);
        set { var v = Math.Clamp(Math.Round(value / 5) * 5, 5, 100) / 100; if (v != S.BackdropOpacity) Change(s => s.BackdropOpacity = v); }
    }

    // ---- The pictures: made while the page is open, again only when what they show changes ----

    public void SetActive(bool active)
    {
        if (active == _active)
            return;
        _active = active;
        if (active)
        {
            _monitor.ThumbnailChanged += OnPicturesChanged;
            _theme.Changed += OnPicturesChanged;
            _settings.Changed += OnSettingsChanged;
            OnSettingsChanged(this, EventArgs.Empty);
        }
        else
        {
            _monitor.ThumbnailChanged -= OnPicturesChanged;
            _theme.Changed -= OnPicturesChanged;
            _settings.Changed -= OnSettingsChanged;
            _refresh?.Abort();
            _refresh = null;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
        OnPicturesChanged(sender, e);
    }

    private void OnPicturesChanged(object? sender, EventArgs e)
    {
        if (!_active || _refresh is not null)
            return;
        _refresh = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _refresh = null;
            if (_active)
                UpdatePictures();
        }, DispatcherPriority.Background);
    }

    private void UpdatePictures()
    {
        var text = ((SolidColorBrush)Application.Current.FindResource("TextPrimaryBrush")).Color;
        if (_diagramColor != text)
        {
            _diagramColor = text;
            foreach (var o in LinesOptions) o.Thumbnail = MiniPlayerLookThumbnails.MinimalLines(o.Value, text);
            foreach (var o in Highlights) o.Thumbnail = MiniPlayerLookThumbnails.Highlight(o.Value, text);
            foreach (var o in Motions) o.Thumbnail = MiniPlayerLookThumbnails.Motion(o.Value, text);
            foreach (var o in Alignments) o.Thumbnail = MiniPlayerLookThumbnails.Alignment(o.Value, text);
            foreach (var o in Typefaces) o.Thumbnail = MiniPlayerLookThumbnails.Typeface(o.Value, text);
            foreach (var o in PausedOptions) o.Thumbnail = MiniPlayerLookThumbnails.MinimalPausedPicture(o.Value, text);
        }

        // The scenes use the song's cover and colours, and the other choices of the same group.
        var cover = _monitor.Thumbnail as BitmapSource;
        var palette = _monitor.Palette;
        var key = (cover, palette, S.Backdrop, S.BackdropOpacity, S.TextColor, S.Legibility);
        if (Equals(_sceneKey, key))
            return;
        _sceneKey = key;
        var verse = LocalizationService.Get("MinimalSample");
        foreach (var o in Backdrops)
            o.Thumbnail = MiniPlayerLookThumbnails.MinimalScene(o.Value, S.BackdropOpacity, S.TextColor, S.Legibility, cover, palette, verse);
        foreach (var o in TextColors)
            o.Thumbnail = MiniPlayerLookThumbnails.MinimalScene(S.Backdrop, S.BackdropOpacity, o.Value, S.Legibility, cover, palette, verse);
        foreach (var o in Legibilities)
            o.Thumbnail = MiniPlayerLookThumbnails.MinimalScene(MinimalBackdrop.Clear, 0, S.TextColor, o.Value, cover, palette, verse);
    }
}
