using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
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
    private string _label = label;

    public T Value { get; } = value;
    public string Label { get => _label; internal set => Set(ref _label, value); }

    /// <summary>Made by the user (the templates mark them discreetly).</summary>
    public bool IsPersonal { get; init; }
    public ImageSource? Thumbnail { get => _thumbnail; internal set => Set(ref _thumbnail, value); }
}

/// <summary>The mini player's appearance settings and its song-aware previews.</summary>
public sealed class MiniPlayerLookViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ISpotifyMonitor _monitor;
    private readonly LyricsSyncService _lyrics;
    private readonly ThemeService _theme;
    private readonly PlayerSurface _surface;
    private DispatcherOperation? _refresh;
    private bool _active;
    private bool _showLooks;
    private (Color Start, Color End)? _pickerPalette;
    private (BitmapSource? Cover, Color Start, Color End, Color Theme, bool AlbumGradient, BackdropTuning Tuning)? _backgroundKey;
    private Color? _diagramColor;
    private Brush? _pickerBackground;
    private object? _presetsKey;
    private object? _presentationBackgroundKey;
    private object? _presentationLyricsKey;
    private Brush? _presentationPreviewBackground;

    public MiniPlayerLookViewModel(SettingsService settings, ISpotifyMonitor monitor, LyricsSyncService lyrics, ThemeService theme, PlayerSurface surface = PlayerSurface.Mini)
    {
        _settings = settings;
        _monitor = monitor;
        _lyrics = lyrics;
        _theme = theme;
        _surface = surface;
        Elements = new BarLayoutViewModel(settings, s => s.PlayerElements(s.EffectivePlayerSurface(surface)), "SongTitle", "Artist", 310, 640,
            taskbar: false, observeSettings: false);
        Elements.Edited += (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty);
        Layouts =
        [
            new(MiniPlayerLayout.Vertical, LocalizationService.Get("LookLayoutVertical")),
            new(MiniPlayerLayout.Cinema, LocalizationService.Get("LookLayoutCinema")),
        ];
        Backgrounds = Options<MiniPlayerBackground>("LookBackground");
        Presets = new ObservableCollection<LookOption<LookPreset>>(
            LookPreset.All.Select(preset => new LookOption<LookPreset>(preset, LocalizationService.Get("LookPreset" + preset.Key))));
        foreach (var personal in SavedLooks)
            Presets.Add(PersonalOption(personal));
        SaveLookCommand = new RelayCommand(SaveLook);
        UpdateLookCommand = new RelayCommand(() =>
        {
            if (Editing?.Value.Personal is not { } p)
                return;
            _settings.Update(s => (p.Look, p.Elements) = (s.PlayerLook(_surface).Clone(), s.PlayerElements(_surface).Clone()));
            _presetsKey = null;
            OnPropertyChanged(nameof(Preset));
            OnPreviewChanged(this, EventArgs.Empty);
        }, () => Editing is not null);
        DeleteLookCommand = new RelayCommand(() =>
        {
            if (Editing is not { Value.Personal: { } p } option)
                return;
            Presets.Remove(option);
            Editing = null;
            _settings.Update(s => s.PlayerPresets(_surface).Remove(p));
            OnPropertyChanged(nameof(Preset));
        }, () => Editing is not null);
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

    private MiniPlayerStyle PreviewLook => _settings.Current.PlayerLook(_settings.Current.EffectivePlayerSurface(_surface));
    public Brush? PresentationPreviewBackground { get => _presentationPreviewBackground; private set => Set(ref _presentationPreviewBackground, value); }
    public Brush PresentationPreviewInk => PreviewLook.Background == MiniPlayerBackground.Theme
        ? (Brush)Application.Current.FindResource("TextPrimaryBrush") : Brushes.White;
    public Brush PresentationPreviewPlayInk => PreviewLook.Background == MiniPlayerBackground.Theme
        ? (Brush)Application.Current.FindResource("WindowBackgroundBrush") : Brushes.Black;
    public ImageSource? PresentationPreviewCover => _monitor.LargeThumbnail ?? PreviewCover;
    public FontFamily PresentationPreviewFont => MiniPlayerViewModel.FontFor(PreviewLook.Typeface);
    public FontFamily PresentationPreviewTitleFont => PreviewLook.Typeface == LyricsTypeface.System
        ? (FontFamily)Application.Current.FindResource("UiDisplayFont") : PresentationPreviewFont;
    public double PresentationPreviewLyricsSize => MiniPlayerViewModel.PresentationLyricsSize(360, PreviewLook);
    public TextAlignment PresentationPreviewAlignment => PreviewLook.Alignment == LyricsAlignment.Left ? TextAlignment.Left : TextAlignment.Center;
    public Point PresentationPreviewOrigin => new(PreviewLook.Alignment == LyricsAlignment.Left ? 0 : 0.5, 0.5);
    public bool PresentationPreviewKaraoke => PreviewLook.ShowKaraokeButton;
    public bool PresentationPreviewSolidControls => PreviewLook.Controls == ControlsLook.Solid;
    public LyricLineViewModel? PresentationPreviewLyric { get; private set; }
    public LyricLineViewModel? PresentationPreviewPrevious { get; private set; }
    public LyricLineViewModel? PresentationPreviewNext { get; private set; }
    public string PresentationPreviewPosition => MiniPlayerViewModel.Format(_monitor.Position);
    public string PresentationPreviewDuration => _monitor.Duration is { TotalSeconds: > 0 } duration ? MiniPlayerViewModel.Format(duration) : "--:--";
    public double PresentationPreviewProgress => _monitor.Duration is { TotalSeconds: > 0 } duration
        ? Math.Clamp(_monitor.Position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;

    private string? VerseAt(int index) => _lyrics.Timeline is { } timeline && index >= 0 && index < timeline.Count
        ? timeline.Lines[index].Text : null;

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
        Elements.Refresh();
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

    public BarLayoutViewModel Elements { get; }
    public bool SupportsLayoutChoice => _surface != PlayerSurface.Player;

    private MiniPlayerStyle Look => _settings.Current.PlayerLook(_surface);
    private List<MiniPlayerPreset> SavedLooks => _settings.Current.PlayerPresets(_surface);

    public bool ShowKaraokeButton
    {
        get => Look.ShowKaraokeButton;
        set { if (value != Look.ShowKaraokeButton) Change(s => s.PlayerLook(_surface).ShowKaraokeButton = value); }
    }

    /// <summary>Something changed: the app shows the mini player, live, beside the settings.</summary>
    public event EventHandler? PreviewRequested;

    private void Change(Action<AppSettings> change)
    {
        _settings.Update(change);
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ready-made looks (background, lyrics and controls together), for picking one instead of
    /// building it, then the user's own; the mode (vertical or wide) is kept. Any part can still
    /// be changed after.
    /// </summary>
    public ObservableCollection<LookOption<LookPreset>> Presets { get; }

    /// <summary>The template the current look is, if it is one (the user's own first: they hold more).</summary>
    public LookOption<LookPreset>? Preset
    {
        get => Presets.FirstOrDefault(p => p.IsPersonal && p.Value.Personal!.Matches(_settings.Current, _surface))
            ?? Presets.FirstOrDefault(p => !p.IsPersonal && p.Value.Matches(Look));
        set
        {
            if (value is null)
                return;
            // Picking one of the user's looks makes it the one the name, update and delete act on.
            Editing = value.IsPersonal ? value : null;
            if (value.Value.Personal is { } personal)
            {
                if (!personal.Matches(_settings.Current, _surface))
                    Change(s => personal.ApplyTo(s, _surface));
            }
            else if (!value.Value.Matches(Look))
                Change(s => value.Value.ApplyTo(s.PlayerLook(_surface)));
        }
    }

    private LookOption<LookPreset>? _editing;

    /// <summary>The user's look being named, updated or deleted (the last one picked or saved).</summary>
    public LookOption<LookPreset>? Editing
    {
        get => _editing;
        private set
        {
            if (Set(ref _editing, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(EditingName));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsEditing => Editing is not null;

    /// <summary>Renamed as it is typed; saved with the settings.</summary>
    public string EditingName
    {
        get => Editing?.Label ?? "";
        set
        {
            if (Editing is not { Value.Personal: { } p } option || value == p.Name)
                return;
            var name = value.Length > 40 ? value[..40] : value;
            option.Label = name;
            _settings.Update(_ => p.Name = name);
        }
    }

    public ICommand SaveLookCommand { get; }
    public ICommand UpdateLookCommand { get; }
    public ICommand DeleteLookCommand { get; }

    /// <summary>A look was saved: the view puts the cursor in its name.</summary>
    public event EventHandler? LookSaved;

    private void SaveLook()
    {
        var number = SavedLooks.Count + 1;
        string name;
        do name = string.Format(LocalizationService.Get("LookPersonalName"), number++);
        while (SavedLooks.Any(p => p.Name == name));
        var personal = MiniPlayerPreset.From(name, _settings.Current, _surface);
        _settings.Update(s => s.PlayerPresets(_surface).Add(personal));
        var option = PersonalOption(personal);
        Presets.Add(option);
        Editing = option;
        OnPropertyChanged(nameof(Preset));
        _presetsKey = null;
        OnPreviewChanged(this, EventArgs.Empty);
        LookSaved?.Invoke(this, EventArgs.Empty);
    }

    private static LookOption<LookPreset> PersonalOption(MiniPlayerPreset personal) =>
        new(LookPreset.FromPersonal(personal), personal.Name) { IsPersonal = true };

    public IReadOnlyList<LookOption<MiniPlayerLayout>> Layouts { get; }

    public IReadOnlyList<LookOption<MiniPlayerBackground>> Backgrounds { get; }

    public IReadOnlyList<LookOption<LyricMotionMode>> Motions { get; }

    public LookOption<LyricMotionMode>? Motion
    {
        get => Motions.FirstOrDefault(option => option.Value == Look.Motion);
        set { if (value is not null && value.Value != Look.Motion) Change(s => s.PlayerLook(_surface).Motion = value.Value); }
    }

    public IReadOnlyList<LookOption<LyricsHighlight>> Highlights { get; }

    public IReadOnlyList<LookOption<LyricsTypeface>> Typefaces { get; }

    public IReadOnlyList<LookOption<LyricsAlignment>> Alignments { get; }

    public IReadOnlyList<LookOption<ControlsLook>> Controls { get; }

    public LookOption<MiniPlayerLayout>? Layout
    {
        get => Layouts.FirstOrDefault(o => o.Value == Look.Layout);
        set { if (value is not null && value.Value != Look.Layout) Change(s => s.PlayerLook(_surface).Layout = value.Value); }
    }

    public LookOption<MiniPlayerBackground>? Background
    {
        get => Backgrounds.FirstOrDefault(o => o.Value == Look.Background);
        set { if (value is not null && value.Value != Look.Background) Change(s => s.PlayerLook(_surface).Background = value.Value); }
    }

    public LookOption<LyricsHighlight>? Highlight
    {
        get => Highlights.FirstOrDefault(o => o.Value == Look.Highlight);
        set { if (value is not null && value.Value != Look.Highlight) Change(s => s.PlayerLook(_surface).Highlight = value.Value); }
    }

    public LookOption<LyricsTypeface>? Typeface
    {
        get => Typefaces.FirstOrDefault(o => o.Value == Look.Typeface);
        set { if (value is not null && value.Value != Look.Typeface) Change(s => s.PlayerLook(_surface).Typeface = value.Value); }
    }

    public LookOption<LyricsAlignment>? Alignment
    {
        get => Alignments.FirstOrDefault(o => o.Value == Look.Alignment);
        set { if (value is not null && value.Value != Look.Alignment) Change(s => s.PlayerLook(_surface).Alignment = value.Value); }
    }

    public LookOption<ControlsLook>? ControlsStyle
    {
        get => Controls.FirstOrDefault(o => o.Value == Look.Controls);
        set { if (value is not null && value.Value != Look.Controls) Change(s => s.PlayerLook(_surface).Controls = value.Value); }
    }

    // ---- The background's intensity: only what the chosen background has is enabled ----

    private (bool Effect, bool Dim, bool Blur) Tunable =>
        MiniPlayerStyle.Tunable(MiniPlayerBackdrop.ResolveStyle(Look.Background, _surface), _settings.Current.Background == OverlayBackground.AlbumGradient);

    public bool HasEffect => Tunable.Effect;
    public bool HasDim => Tunable.Dim;
    public bool HasBlur => Tunable.Blur;
    public bool NoEffect => !HasEffect;
    public bool NoDim => !HasDim;
    public bool NoBlur => !HasBlur;

    public double EffectPercent
    {
        get => Math.Round(MiniPlayerStyle.ClampEffect(Look.BackgroundEffect) * 100);
        set { var v = MiniPlayerStyle.ClampEffect(value / 100); if (v != Look.BackgroundEffect) Change(s => s.PlayerLook(_surface).BackgroundEffect = v); }
    }

    public double DimPercent
    {
        get => Math.Round(MiniPlayerStyle.ClampDim(Look.BackgroundDim) * 100);
        set { var v = MiniPlayerStyle.ClampDim(value / 100); if (v != Look.BackgroundDim) Change(s => s.PlayerLook(_surface).BackgroundDim = v); }
    }

    public bool InterludeDots { get => Look.InterludeDots; set { if (value != Look.InterludeDots) Change(s => s.PlayerLook(_surface).InterludeDots = value); } }

    /// <summary>The depth highlight is chosen: its light and blur can be tuned.</summary>
    public bool IsDepth => Look.Highlight == LyricsHighlight.Depth;

    public double GlowPercent
    {
        get => Math.Round(MiniPlayerStyle.ClampGlow(Look.LyricsGlow) * 100);
        set { var v = MiniPlayerStyle.ClampGlow(value / 100); if (v != Look.LyricsGlow) Change(s => s.PlayerLook(_surface).LyricsGlow = v); }
    }

    public double DepthPercent
    {
        get => Math.Round(MiniPlayerStyle.ClampDepth(Look.LyricsDepth) * 100);
        set { var v = MiniPlayerStyle.ClampDepth(value / 100); if (v != Look.LyricsDepth) Change(s => s.PlayerLook(_surface).LyricsDepth = v); }
    }

    public double BlurPercent
    {
        get => Math.Round(MiniPlayerStyle.ClampBlur(Look.BackgroundBlur) * 100);
        set { var v = MiniPlayerStyle.ClampBlur(value / 100); if (v != Look.BackgroundBlur) Change(s => s.PlayerLook(_surface).BackgroundBlur = v); }
    }

    /// <summary>The lyrics' size in percent.</summary>
    public double LyricsPercent
    {
        get => Math.Round(Look.LyricsScale * 100);
        set => Change(s => s.PlayerLook(_surface).LyricsScale = Math.Clamp(Math.Round(value / 5) * 5, 70, 220) / 100);
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
        if (_surface == PlayerSurface.Player)
            UpdatePresentationPreview();
        if (!_showLooks)
            return;

        var themeBackground = ResourceColor("PopupBackgroundBrush");
        var themeText = ResourceColor("TextPrimaryBrush");
        var albumGradient = _settings.Current.Background == OverlayBackground.AlbumGradient;
        var tuning = BackdropTuning.From(Look);
        var key = (cover, palette.Item1, palette.Item2, themeBackground, albumGradient, tuning);
        if (_backgroundKey != key)
        {
            _backgroundKey = key;
            // The pictures show the intensity chosen, on every background it applies to.
            foreach (var option in Backgrounds)
                option.Thumbnail = MiniPlayerLookThumbnails.Background(option.Value, cover, palette, themeBackground, albumGradient, tuning);
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

        var index = _lyrics.CurrentIndex;
        var sample = new MiniPlayerLookSample(PreviewTitle, PreviewArtist, cover, VerseAt(index - 1), VerseAt(index), VerseAt(index + 1),
            _monitor.IsPlaying, _monitor.Duration is { TotalSeconds: > 0 } duration
                ? Math.Clamp(_monitor.Position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0);
        ImageSource Backdrop(MiniPlayerBackground background) => Backgrounds.First(option => option.Value == background).Thumbnail!;
        Color Ink(MiniPlayerBackground background) =>
            background == MiniPlayerBackground.Theme || (background == MiniPlayerBackground.Default && !albumGradient)
                ? themeText : Colors.White;

        // The pictures show the song's verses: redrawn when those, the cover or a look changes, not on every event.
        var current = new LookPreset("", Look.Background, Look.Highlight, Look.Typeface, Look.Alignment, Look.Controls);
        var presetsKey = (sample, Look.Layout, current, key, Presets.Count);
        if (Equals(_presetsKey, presetsKey))
            return;
        _presetsKey = presetsKey;
        foreach (var option in Layouts)
            option.Thumbnail = MiniPlayerLookThumbnails.Player(option.Value, current, sample, Backdrop(current.Background), Ink(current.Background));
        foreach (var option in Presets)
        {
            var look = option.Value.Personal is { } p ? LookPreset.FromPersonal(p) : option.Value;
            // A personal look is drawn with the background intensity it was saved with.
            var backdrop = option.Value.Personal is { } own && BackdropTuning.From(own.Look) != tuning
                ? MiniPlayerLookThumbnails.Background(look.Background, cover, palette, themeBackground, albumGradient, BackdropTuning.From(own.Look))
                : Backdrop(look.Background);
            option.Thumbnail = MiniPlayerLookThumbnails.Player(SupportsLayoutChoice ? Look.Layout : MiniPlayerLayout.Cinema, look, sample, backdrop, Ink(look.Background));
        }

    }

    private void UpdatePresentationPreview()
    {
        var cover = PresentationPreviewCover as BitmapSource;
        var style = MiniPlayerBackdrop.ResolveStyle(PreviewLook.Background, PlayerSurface.Player);
        var tuning = BackdropTuning.From(PreviewLook);
        var theme = ResourceColor("PopupBackgroundBrush");
        var palette = _monitor.Palette;
        var key = (cover, style, tuning, theme, palette);
        if (!Equals(_presentationBackgroundKey, key))
        {
            _presentationBackgroundKey = key;
            PresentationPreviewBackground = style == MiniPlayerBackground.Theme
                ? (Brush)Application.Current.FindResource("PopupBackgroundBrush")
                : MiniPlayerBackdrop.AlbumBrush(cover, style, palette, tuning, 192, 108);
        }
        foreach (var name in new[] { nameof(PresentationPreviewInk), nameof(PresentationPreviewPlayInk), nameof(PresentationPreviewCover),
                     nameof(PresentationPreviewLyricsSize), nameof(PresentationPreviewFont),
                     nameof(PresentationPreviewTitleFont), nameof(PresentationPreviewAlignment), nameof(PresentationPreviewOrigin),
                     nameof(PresentationPreviewKaraoke), nameof(PresentationPreviewSolidControls), nameof(PresentationPreviewPosition), nameof(PresentationPreviewDuration),
                     nameof(PresentationPreviewProgress) })
            OnPropertyChanged(name);

        var look = PreviewLook;
        var index = _lyrics.CurrentIndex;
        var previous = VerseAt(index - 1);
        var current = _lyrics.CurrentLine?.Text ?? LocalizationService.Get("MinimalSample");
        var next = VerseAt(index + 1);
        var scale = PresentationPreviewLyricsSize / 23;
        var lyricsKey = (previous, current, next, look.Highlight, look.Motion, look.Typeface, look.LyricsGlow, look.LyricsDepth, scale);
        if (Equals(_presentationLyricsKey, lyricsKey))
            return;
        _presentationLyricsKey = lyricsKey;

        LyricLineViewModel? PreviewLine(string? text, int distance)
        {
            if (text is null)
                return null;
            var line = new LyricLineViewModel(0, TimeSpan.Zero, text);
            line.Emphasize(look.Highlight, distance, started: true, heavy: look.Typeface != LyricsTypeface.System,
                motion: look.Motion, size: scale, glow: look.LyricsGlow, depth: look.LyricsDepth);
            return line;
        }
        PresentationPreviewPrevious = PreviewLine(previous, -1);
        PresentationPreviewLyric = PreviewLine(current, 0);
        PresentationPreviewNext = PreviewLine(next, 1);
        OnPropertyChanged(nameof(PresentationPreviewPrevious));
        OnPropertyChanged(nameof(PresentationPreviewLyric));
        OnPropertyChanged(nameof(PresentationPreviewNext));
    }

    private static Color ResourceColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
}

/// <summary>A ready-made mini player look: background, lyrics and controls that go together.</summary>
public sealed record LookPreset(string Key, MiniPlayerBackground Background, LyricsHighlight Highlight, LyricsTypeface Typeface,
    LyricsAlignment Alignment, ControlsLook Controls, MiniPlayerPreset? Personal = null)
{
    /// <summary>A user's look, as the templates' pictures draw it (from what it holds now).</summary>
    public static LookPreset FromPersonal(MiniPlayerPreset p) =>
        new("", p.Look.Background, p.Look.Highlight, p.Look.Typeface, p.Look.Alignment, p.Look.Controls, p);

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
