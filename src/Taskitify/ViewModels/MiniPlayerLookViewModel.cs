using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Models;
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
        Highlights = Enum.GetValues<LyricsHighlight>().Select(h => new LookOption<LyricsHighlight>(h, LocalizationService.Get("LookHighlight" + h))).ToList();
        Typefaces = Enum.GetValues<LyricsTypeface>().Select(t => new LookOption<LyricsTypeface>(t, LocalizationService.Get("LookTypeface" + t))).ToList();
        Alignments = Enum.GetValues<LyricsAlignment>().Select(a => new LookOption<LyricsAlignment>(a, LocalizationService.Get("LookAlign" + a))).ToList();
        Controls = Enum.GetValues<ControlsLook>().Select(c => new LookOption<ControlsLook>(c, LocalizationService.Get("LookControls" + c))).ToList();
        _settings.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    private MiniPlayerStyle Look => _settings.Current.MiniPlayerLook;

    public IReadOnlyList<LookOption<MiniPlayerLayout>> Layouts { get; }

    public IReadOnlyList<LookOption<MiniPlayerBackground>> Backgrounds { get; }

    public IReadOnlyList<LookOption<LyricsHighlight>> Highlights { get; }

    public IReadOnlyList<LookOption<LyricsTypeface>> Typefaces { get; }

    public IReadOnlyList<LookOption<LyricsAlignment>> Alignments { get; }

    public IReadOnlyList<LookOption<ControlsLook>> Controls { get; }

    public LookOption<MiniPlayerLayout>? Layout
    {
        get => Layouts.FirstOrDefault(o => o.Value == Look.Layout);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Layout = value.Value); }
    }

    public LookOption<MiniPlayerBackground>? Background
    {
        get => Backgrounds.FirstOrDefault(o => o.Value == Look.Background);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Background = value.Value); }
    }

    public LookOption<LyricsHighlight>? Highlight
    {
        get => Highlights.FirstOrDefault(o => o.Value == Look.Highlight);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Highlight = value.Value); }
    }

    public LookOption<LyricsTypeface>? Typeface
    {
        get => Typefaces.FirstOrDefault(o => o.Value == Look.Typeface);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Typeface = value.Value); }
    }

    public LookOption<LyricsAlignment>? Alignment
    {
        get => Alignments.FirstOrDefault(o => o.Value == Look.Alignment);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Alignment = value.Value); }
    }

    public LookOption<ControlsLook>? ControlsStyle
    {
        get => Controls.FirstOrDefault(o => o.Value == Look.Controls);
        set { if (value is not null) _settings.Update(s => s.MiniPlayerLook.Controls = value.Value); }
    }

    /// <summary>The lyrics' size in percent.</summary>
    public double LyricsPercent
    {
        get => Math.Round(Look.LyricsScale * 100);
        set => _settings.Update(s => s.MiniPlayerLook.LyricsScale = Math.Clamp(Math.Round(value / 5) * 5, 80, 140) / 100);
    }

    // ---- thumbnails ----

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
