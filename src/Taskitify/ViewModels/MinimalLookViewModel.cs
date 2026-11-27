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
    public bool AutoFit { get => S.AutoFit; set => Change(s => s.AutoFit = value); }

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
