using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Models;
using Taskitify.Core.Lyrics;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

/// <summary>A choice in the appearance settings: its value, name and, for layouts and backgrounds, a picture of it.</summary>
public sealed record LookOption<T>(T Value, string Label, ImageSource? Thumbnail = null);

/// <summary>
/// Settings > Mini player > Appearance: layout, background, lyrics and controls, each chosen on
/// its own and saved at once. Thumbnails are drawn when the settings open (a few small images),
/// the backgrounds with the cover playing now.
/// </summary>
public sealed class MiniPlayerLookViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public MiniPlayerLookViewModel(SettingsService settings, BitmapSource? cover, (Color Start, Color End) palette)
    {
        _settings = settings;
        Layouts =
        [
            new(MiniPlayerLayout.Vertical, LocalizationService.Get("LookLayoutVertical"), LayoutThumbnail(cinema: false)),
            new(MiniPlayerLayout.Cinema, LocalizationService.Get("LookLayoutCinema"), LayoutThumbnail(cinema: true)),
        ];
        Backgrounds = Enum.GetValues<MiniPlayerBackground>()
            .Select(b => new LookOption<MiniPlayerBackground>(b, LocalizationService.Get("LookBackground" + b), BackgroundThumbnail(b, cover, palette)))
            .ToList();
        Presets = LookPreset.All
            .Select(p => new LookOption<LookPreset>(p, LocalizationService.Get("LookPreset" + p.Key), PresetThumbnail(p, cover, palette)))
            .ToList();
        Flows = Enum.GetValues<LyricFlowMode>().Select(mode => new LookOption<LyricFlowMode>(mode, LocalizationService.Get("Flow" + mode))).ToList();
        Motions = Enum.GetValues<LyricMotionMode>().Select(mode => new LookOption<LyricMotionMode>(mode, LocalizationService.Get("Motion" + mode))).ToList();
        Highlights = Enum.GetValues<LyricsHighlight>().Select(h => new LookOption<LyricsHighlight>(h, LocalizationService.Get("LookHighlight" + h))).ToList();
        Typefaces = Enum.GetValues<LyricsTypeface>().Select(t => new LookOption<LyricsTypeface>(t, LocalizationService.Get("LookTypeface" + t))).ToList();
        Alignments = Enum.GetValues<LyricsAlignment>().Select(a => new LookOption<LyricsAlignment>(a, LocalizationService.Get("LookAlign" + a))).ToList();
        Controls = Enum.GetValues<ControlsLook>().Select(c => new LookOption<ControlsLook>(c, LocalizationService.Get("LookControls" + c))).ToList();
        _settings.Changed += (_, _) => OnPropertyChanged(string.Empty);
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
        set { if (value is not null) Change(s => value.Value.ApplyTo(s.MiniPlayerLook)); }
    }

    public IReadOnlyList<LookOption<MiniPlayerLayout>> Layouts { get; }

    public IReadOnlyList<LookOption<MiniPlayerBackground>> Backgrounds { get; }

    public IReadOnlyList<LookOption<LyricFlowMode>> Flows { get; }
    public IReadOnlyList<LookOption<LyricMotionMode>> Motions { get; }

    public LookOption<LyricFlowMode>? Flow
    {
        get => Flows.FirstOrDefault(option => option.Value == Look.Flow);
        set { if (value is not null && value.Value != Look.Flow) Change(s => s.MiniPlayerLook.Flow = value.Value); }
    }

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
        set { if (value is not null) Change(s => s.MiniPlayerLook.Layout = value.Value); }
    }

    public LookOption<MiniPlayerBackground>? Background
    {
        get => Backgrounds.FirstOrDefault(o => o.Value == Look.Background);
        set { if (value is not null) Change(s => s.MiniPlayerLook.Background = value.Value); }
    }

    public LookOption<LyricsHighlight>? Highlight
    {
        get => Highlights.FirstOrDefault(o => o.Value == Look.Highlight);
        set { if (value is not null) Change(s => s.MiniPlayerLook.Highlight = value.Value); }
    }

    public LookOption<LyricsTypeface>? Typeface
    {
        get => Typefaces.FirstOrDefault(o => o.Value == Look.Typeface);
        set { if (value is not null) Change(s => s.MiniPlayerLook.Typeface = value.Value); }
    }

    public LookOption<LyricsAlignment>? Alignment
    {
        get => Alignments.FirstOrDefault(o => o.Value == Look.Alignment);
        set { if (value is not null) Change(s => s.MiniPlayerLook.Alignment = value.Value); }
    }

    public LookOption<ControlsLook>? ControlsStyle
    {
        get => Controls.FirstOrDefault(o => o.Value == Look.Controls);
        set { if (value is not null) Change(s => s.MiniPlayerLook.Controls = value.Value); }
    }

    /// <summary>The lyrics' size in percent.</summary>
    public double LyricsPercent
    {
        get => Math.Round(Look.LyricsScale * 100);
        set => Change(s => s.MiniPlayerLook.LyricsScale = Math.Clamp(Math.Round(value / 5) * 5, 80, 140) / 100);
    }

    // ---- thumbnails ----

    /// <summary>The preset's background with sketched lyrics (its alignment and emphasis) and controls.</summary>
    private static ImageSource PresetThumbnail(LookPreset preset, BitmapSource? cover, (Color Start, Color End) palette)
    {
        const double w = 72, h = 96;
        var background = BackgroundThumbnail(preset.Background, cover, palette);
        var left = preset.Alignment == LyricsAlignment.Left;
        var white = Color.FromRgb(0xFF, 0xFF, 0xFF);
        return Draw(w, h, dc =>
        {
            dc.DrawImage(background, new Rect(0, 0, w, h));
            if (cover is not null)
                dc.DrawImage(cover, new Rect(7, 7, 14, 14));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), null, new Rect(25, 9, 24, 3.5));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), null, new Rect(25, 15, 16, 2.5));
            double[] widths = [40, 46, 52, 38, 44];
            for (var i = 0; i < widths.Length; i++)
            {
                var current = i == 2;
                var alpha = preset.Highlight switch
                {
                    LyricsHighlight.Progressive => i == 2 ? 0xFF : Math.Abs(i - 2) == 1 ? 0x90 : 0x50,
                    _ => current ? 0xFF : 0x60,
                };
                var lineW = widths[i] * (current && preset.Highlight == LyricsHighlight.Expressive ? 1.15 : 1);
                var lineH = current && preset.Highlight == LyricsHighlight.Expressive ? 4.5 : 3;
                var x = left ? 8 : (w - lineW) / 2;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)alpha, white.R, white.G, white.B)), null, new Rect(x, 30 + (i * 8), lineW, lineH));
            }
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), null, new Rect(8, 74, 56, 2));
            var play = new SolidColorBrush(white);
            if (preset.Controls == ControlsLook.Solid)
                dc.DrawGeometry(play, null, Geometry.Parse("M33,80 L41,85 L33,90 Z"));
            else
                dc.DrawEllipse(play, null, new Point(36, 85), 5, 5);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), null, new Rect(20, 83, 6, 4));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), null, new Rect(47, 83, 6, 4));
        });
    }

    private static ImageSource BackgroundThumbnail(MiniPlayerBackground style, BitmapSource? cover, (Color Start, Color End) palette)
    {
        if (MiniPlayerBackdrop.IsAtmospheric(style))
            return MiniPlayerBackdrop.Render(cover, style, 72, 96);
        var themeColor = Application.Current.TryFindResource("PopupBackgroundBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0x20, 0x20, 0x20);
        Brush fill = style switch
        {
            MiniPlayerBackground.Theme => new SolidColorBrush(themeColor),
            _ => new LinearGradientBrush(palette.End, palette.Start, new Point(0, 0), new Point(1, 1)),
        };
        return Draw(72, 96, dc => dc.DrawRectangle(fill, null, new Rect(0, 0, 72, 96)));
    }

    /// <summary>A sketch of the layout: where the cover, the lyrics and the controls go.</summary>
    private static ImageSource LayoutThumbnail(bool cinema)
    {
        var w = cinema ? 128.0 : 72;
        var h = cinema ? 76.0 : 96;
        var card = new SolidColorBrush(Color.FromRgb(0x2E, 0x34, 0x40));
        var line = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        var bright = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        var cover = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF));
        return Draw(w, h, dc =>
        {
            dc.DrawRoundedRectangle(card, null, new Rect(0, 0, w, h), 6, 6);
            if (!cinema)
            {
                dc.DrawRoundedRectangle(cover, null, new Rect(8, 8, 16, 16), 2, 2);
                dc.DrawRectangle(bright, null, new Rect(28, 10, 26, 4));
                dc.DrawRectangle(line, null, new Rect(28, 17, 18, 3));
                for (var i = 0; i < 4; i++)
                    dc.DrawRectangle(i == 1 ? bright : line, null, new Rect(i == 1 ? 12 : 16, 32 + (i * 8), i == 1 ? 48 : 40, 3.5));
                dc.DrawRectangle(line, null, new Rect(8, 70, 56, 2));
                dc.DrawEllipse(bright, null, new Point(36, 84), 5, 5);
                dc.DrawRectangle(line, null, new Rect(20, 82, 6, 4));
                dc.DrawRectangle(line, null, new Rect(46, 82, 6, 4));
            }
            else
            {
                dc.DrawRoundedRectangle(cover, null, new Rect(8, 8, 34, 34), 3, 3);
                dc.DrawRectangle(bright, null, new Rect(8, 46, 26, 3.5));
                dc.DrawRectangle(line, null, new Rect(8, 52, 18, 3));
                for (var i = 0; i < 5; i++)
                    dc.DrawRectangle(i == 2 ? bright : line, null, new Rect(52, 10 + (i * 9), i == 2 ? 64 : 52, 4));
                dc.DrawRectangle(line, null, new Rect(8, 62, 112, 2));
                dc.DrawEllipse(bright, null, new Point(64, 70), 3.5, 3.5);
                dc.DrawRectangle(line, null, new Rect(50, 68.5, 5, 3));
                dc.DrawRectangle(line, null, new Rect(73, 68.5, 5, 3));
            }
        });
    }

    private static ImageSource Draw(double w, double h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            draw(dc);
        // Twice the size, for sharp edges on high-DPI screens.
        var bitmap = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
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
