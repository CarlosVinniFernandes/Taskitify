using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

/// <summary>An appearance choice with a picture that can follow the song without replacing the selection.</summary>
public sealed class LookOption<T>(T value, string label) : ObservableObject
{
    private ImageSource? _thumbnail;

    public T Value { get; } = value;
    public string Label { get; } = label;
    public ImageSource? Thumbnail { get => _thumbnail; internal set => Set(ref _thumbnail, value); }
}

/// <summary>The mini player's appearance settings and its song-aware previews.</summary>
public sealed class MiniPlayerLookViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _lyrics;
    private readonly ThemeService _theme;
    private DispatcherOperation? _refresh;
    private bool _active;
    private bool _showLooks;
    private (Color Start, Color End)? _pickerPalette;
    private (BitmapSource? Cover, Color Start, Color End, Color Theme, bool AlbumGradient)? _backgroundKey;
    private Color? _diagramColor;
    private Brush? _pickerBackground;

    public MiniPlayerLookViewModel(SettingsService settings, ISpotifyMonitor monitor, LyricsSyncService lyrics, ThemeService theme)
    {
        _settings = settings;
        _monitor = monitor;
        _lyrics = lyrics;
        _theme = theme;
        Layouts =
        [
            new(MiniPlayerLayout.Vertical, LocalizationService.Get("LookLayoutVertical")),
            new(MiniPlayerLayout.Cinema, LocalizationService.Get("LookLayoutCinema")),
        ];
        Backgrounds = Options<MiniPlayerBackground>("LookBackground");
        Presets = LookPreset.All.Select(preset => new LookOption<LookPreset>(preset, LocalizationService.Get("LookPreset" + preset.Key))).ToArray();
        Motions = Options<LyricMotionMode>("Motion");
        Highlights = Options<LyricsHighlight>("LookHighlight");
        Typefaces = Options<LyricsTypeface>("LookTypeface");
        Alignments = Options<LyricsAlignment>("LookAlign");
        Controls = Options<ControlsLook>("LookControls");
    }

    private static LookOption<T>[] Options<T>(string key) where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new LookOption<T>(value, LocalizationService.Get(key + value))).ToArray();

    public ImageSource? PreviewCover => _monitor.Thumbnail;
    public string PreviewTitle => _monitor.Track?.Title ?? LocalizationService.Get("SongTitle");
    public string PreviewArtist => _monitor.Track?.Artist ?? LocalizationService.Get("Artist");
    public string PreviewLyric => _lyrics.CurrentLine?.Text ?? LocalizationService.Get("LyricsAlwaysShown");
    public string PreviewPlayGlyph => _monitor.IsPlaying ? "\uE769" : "\uE768";
    public string BarPreviewText => BarText.Main;
    public string BarPreviewCaption => BarText.Caption;
    public Brush? PickerBackground { get => _pickerBackground; private set => Set(ref _pickerBackground, value); }

    private (string Main, string Caption) BarText
    {
        get
        {
            if (_monitor.Track is not { } track)
                return (LocalizationService.Get("SongLyrics"), LocalizationService.Get("SongArtist"));
            var verse = _settings.Current.PrimaryText == PrimaryText.Lyrics && _lyrics.State == LyricsState.Synced
                ? _lyrics.CurrentLine?.Text : null;
            return TaskbarViewModel.FormatTrackText(track, verse);
        }
    }

    public void SetPreviewActive(bool active, bool showLooks = true)
    {
        if (active == _active)
        {
            if (active && _showLooks != showLooks)
            {
                _showLooks = showLooks;
                OnPreviewChanged(this, EventArgs.Empty);
            }
            return;
        }
        _showLooks = showLooks;
        _active = active;
        if (active)
        {
            _monitor.TrackChanged += OnPreviewChanged;
            _monitor.ThumbnailChanged += OnPreviewChanged;
            _monitor.PlaybackChanged += OnPreviewChanged;
            _lyrics.LyricsChanged += OnPreviewChanged;
            _lyrics.CurrentLineChanged += OnPreviewChanged;
            _theme.Changed += OnPreviewChanged;
            _settings.Changed += OnSettingsChanged;
            OnSettingsChanged(this, EventArgs.Empty);
        }
        else
        {
            _monitor.TrackChanged -= OnPreviewChanged;
            _monitor.ThumbnailChanged -= OnPreviewChanged;
            _monitor.PlaybackChanged -= OnPreviewChanged;
            _lyrics.LyricsChanged -= OnPreviewChanged;
            _lyrics.CurrentLineChanged -= OnPreviewChanged;
            _theme.Changed -= OnPreviewChanged;
            _settings.Changed -= OnSettingsChanged;
            _refresh?.Abort();
            _refresh = null;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
        OnPreviewChanged(sender, e);
    }

    private void OnPreviewChanged(object? sender, EventArgs e)
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

    private MiniPlayerStyle Look => _settings.Current.MiniPlayerLook;

    /// <summary>Something changed: the app shows the mini player, live, beside the settings.</summary>
    public event EventHandler? PreviewRequested;

    private void Change(Action<AppSettings> change)
    {
        _settings.Update(change);
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ready-made looks (background, lyrics and controls together), for picking one instead of
    /// building it; the mode (vertical or wide) is kept. Any part can still be changed after.
    /// </summary>
    public IReadOnlyList<LookOption<LookPreset>> Presets { get; }

    /// <summary>The preset the current look is, if it is one.</summary>
    public LookOption<LookPreset>? Preset
    {
        get => Presets.FirstOrDefault(p => p.Value.Matches(Look));
        set { if (value is not null && !value.Value.Matches(Look)) Change(s => value.Value.ApplyTo(s.MiniPlayerLook)); }
    }

    public IReadOnlyList<LookOption<MiniPlayerLayout>> Layouts { get; }

    public IReadOnlyList<LookOption<MiniPlayerBackground>> Backgrounds { get; }

    public IReadOnlyList<LookOption<LyricMotionMode>> Motions { get; }

    public LookOption<LyricMotionMode>? Motion
    {
        get => Motions.FirstOrDefault(option => option.Value == Look.Motion);
        set { if (value is not null && value.Value != Look.Motion) Change(s => s.MiniPlayerLook.Motion = value.Value); }
    }

    public IReadOnlyList<LookOption<LyricsHighlight>> Highlights { get; }

    public IReadOnlyList<LookOption<LyricsTypeface>> Typefaces { get; }

    public IReadOnlyList<LookOption<LyricsAlignment>> Alignments { get; }

    public IReadOnlyList<LookOption<ControlsLook>> Controls { get; }

    public LookOption<MiniPlayerLayout>? Layout
    {
        get => Layouts.FirstOrDefault(o => o.Value == Look.Layout);
        set { if (value is not null && value.Value != Look.Layout) Change(s => s.MiniPlayerLook.Layout = value.Value); }
    }

    public LookOption<MiniPlayerBackground>? Background
    {
        get => Backgrounds.FirstOrDefault(o => o.Value == Look.Background);
        set { if (value is not null && value.Value != Look.Background) Change(s => s.MiniPlayerLook.Background = value.Value); }
    }

    public LookOption<LyricsHighlight>? Highlight
    {
        get => Highlights.FirstOrDefault(o => o.Value == Look.Highlight);
        set { if (value is not null && value.Value != Look.Highlight) Change(s => s.MiniPlayerLook.Highlight = value.Value); }
    }

    public LookOption<LyricsTypeface>? Typeface
    {
        get => Typefaces.FirstOrDefault(o => o.Value == Look.Typeface);
        set { if (value is not null && value.Value != Look.Typeface) Change(s => s.MiniPlayerLook.Typeface = value.Value); }
    }

    public LookOption<LyricsAlignment>? Alignment
    {
        get => Alignments.FirstOrDefault(o => o.Value == Look.Alignment);
        set { if (value is not null && value.Value != Look.Alignment) Change(s => s.MiniPlayerLook.Alignment = value.Value); }
    }

    public LookOption<ControlsLook>? ControlsStyle
    {
        get => Controls.FirstOrDefault(o => o.Value == Look.Controls);
        set { if (value is not null && value.Value != Look.Controls) Change(s => s.MiniPlayerLook.Controls = value.Value); }
    }

    /// <summary>The lyrics' size in percent.</summary>
    public double LyricsPercent
    {
        get => Math.Round(Look.LyricsScale * 100);
        set => Change(s => s.MiniPlayerLook.LyricsScale = Math.Clamp(Math.Round(value / 5) * 5, 80, 140) / 100);
    }

    private void UpdatePictures()
    {
        var cover = PreviewCover as BitmapSource;
        var palette = cover is not null ? _monitor.Palette
            : (Color.FromRgb(32, 38, 46), Color.FromRgb(53, 68, 79));
        if (_pickerPalette != palette)
        {
            _pickerPalette = palette;
            var pickerBackground = new LinearGradientBrush(palette.Item2, palette.Item1, new Point(0, 0), new Point(1, 1));
            pickerBackground.Freeze();
            PickerBackground = pickerBackground;
        }
        OnPropertyChanged(nameof(PreviewCover));
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(PreviewArtist));
        OnPropertyChanged(nameof(PreviewLyric));
        OnPropertyChanged(nameof(PreviewPlayGlyph));
        OnPropertyChanged(nameof(BarPreviewText));
        OnPropertyChanged(nameof(BarPreviewCaption));
        if (!_showLooks)
            return;

        var themeBackground = ResourceColor("PopupBackgroundBrush");
        var themeText = ResourceColor("TextPrimaryBrush");
        var albumGradient = _settings.Current.Background == OverlayBackground.AlbumGradient;
        var key = (cover, palette.Item1, palette.Item2, themeBackground, albumGradient);
        if (_backgroundKey != key)
        {
            _backgroundKey = key;
            foreach (var option in Backgrounds)
                option.Thumbnail = MiniPlayerLookThumbnails.Background(option.Value, cover, palette, themeBackground, albumGradient);
        }

        if (_diagramColor != themeText)
        {
            _diagramColor = themeText;
            foreach (var option in Highlights)
                option.Thumbnail = MiniPlayerLookThumbnails.Highlight(option.Value, themeText);
            foreach (var option in Motions)
                option.Thumbnail = MiniPlayerLookThumbnails.Motion(option.Value, themeText);
            foreach (var option in Alignments)
                option.Thumbnail = MiniPlayerLookThumbnails.Alignment(option.Value, themeText);
            foreach (var option in Typefaces)
                option.Thumbnail = MiniPlayerLookThumbnails.Typeface(option.Value, themeText);
            foreach (var option in Controls)
                option.Thumbnail = MiniPlayerLookThumbnails.Controls(option.Value, themeText);
        }

        var timeline = _lyrics.Timeline;
        var index = _lyrics.CurrentIndex;
        string? LineAt(int i) => timeline is not null && i >= 0 && i < timeline.Count ? timeline.Lines[i].Text : null;
        var sample = new MiniPlayerLookSample(PreviewTitle, PreviewArtist, cover, LineAt(index - 1), LineAt(index), LineAt(index + 1),
            _monitor.IsPlaying, _monitor.Duration is { TotalSeconds: > 0 } duration
                ? Math.Clamp(_monitor.Position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0);
        ImageSource Backdrop(MiniPlayerBackground background) => Backgrounds.First(option => option.Value == background).Thumbnail!;
        Color Ink(MiniPlayerBackground background) =>
            background == MiniPlayerBackground.Theme || (background == MiniPlayerBackground.Default && !albumGradient)
                ? themeText : Colors.White;

        var current = new LookPreset("", Look.Background, Look.Highlight, Look.Typeface, Look.Alignment, Look.Controls);
        foreach (var option in Layouts)
            option.Thumbnail = MiniPlayerLookThumbnails.Player(option.Value, current, sample, Backdrop(current.Background), Ink(current.Background));
        foreach (var option in Presets)
            option.Thumbnail = MiniPlayerLookThumbnails.Player(Look.Layout, option.Value, sample, Backdrop(option.Value.Background), Ink(option.Value.Background));

    }

    private static Color ResourceColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
}

/// <summary>A ready-made mini player look: background, lyrics and controls that go together.</summary>
public sealed record LookPreset(string Key, MiniPlayerBackground Background, LyricsHighlight Highlight, LyricsTypeface Typeface,
    LyricsAlignment Alignment, ControlsLook Controls)
{
    public static IReadOnlyList<LookPreset> All { get; } =
    [
        new("Classic", MiniPlayerBackground.Default, LyricsHighlight.Classic, LyricsTypeface.Modern, LyricsAlignment.Center, ControlsLook.Classic),
        new("Aurora", MiniPlayerBackground.Aurora, LyricsHighlight.Expressive, LyricsTypeface.Modern, LyricsAlignment.Left, ControlsLook.Solid),
        new("Calm", MiniPlayerBackground.Atmosphere, LyricsHighlight.Progressive, LyricsTypeface.Modern, LyricsAlignment.Center, ControlsLook.Classic),
        new("Velvet", MiniPlayerBackground.Velvet, LyricsHighlight.Progressive, LyricsTypeface.Serif, LyricsAlignment.Center, ControlsLook.Solid),
        new("Cover", MiniPlayerBackground.BlurredCover, LyricsHighlight.Expressive, LyricsTypeface.Modern, LyricsAlignment.Center, ControlsLook.Solid),
        new("Minimal", MiniPlayerBackground.Theme, LyricsHighlight.Classic, LyricsTypeface.Geometric, LyricsAlignment.Left, ControlsLook.Classic),
    ];

    public bool Matches(MiniPlayerStyle look) => look.Background == Background && look.Highlight == Highlight && look.Typeface == Typeface
        && look.Alignment == Alignment && look.Controls == Controls;

    public void ApplyTo(MiniPlayerStyle look)
    {
        look.Background = Background;
        look.Highlight = Highlight;
        look.Typeface = Typeface;
        look.Alignment = Alignment;
        look.Controls = Controls;
    }
}
