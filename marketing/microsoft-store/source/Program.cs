using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;
using Taskitify.Views;

// Main Microsoft Store screenshot (3840 × 2160): the real mini player (Cinema wide) and the real
// taskbar player rendered from the production WPF views, composed over a browser and a Windows-style
// taskbar. Runs on an isolated data folder: never reads or writes the user's settings, never talks
// to Spotify or a lyrics provider. Lyrics and cover art are original demonstration material.
//
//   dotnet run --project marketing/microsoft-store/source -- <repo root>
internal static class Program
{
    const int W = 3840, H = 2160;
    static string _out = "";
    static string _fonts = "";

    // The New Abnormal palette given in the brief.
    static readonly Color Petrol = Hex("#142D3C");
    static readonly Color SlateBlue = Hex("#356B79");
    static readonly Color Turquoise = Hex("#189BB3");
    static readonly Color Gold = Hex("#AF9B67");
    static readonly Color Cream = Hex("#F1F2ED");
    static readonly Color TaskbarColor = Hex("#14202A");

    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        _out = Path.Combine(root, "marketing", "microsoft-store");
        _fonts = Path.Combine(root, "src", "Taskitify", "Resources", "Fonts");
        var data = Path.Combine(Path.GetTempPath(), "taskitify-store-shot");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("TASKITIFY_DATA_DIR", data);

        // The app's resources without Taskitify.App itself: its startup would find the running
        // instance and signal it.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Taskitify;component/Resources/Styles.xaml") });
        resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        resources["NullToCollapsed"] = new Taskitify.Infrastructure.NullToCollapsedConverter();
        app.Resources = resources;
        using var theme = new ThemeService();
        LocalizationService.SetLanguage("en");

        // Internal preview: the reference song's name. Public: a demonstration track. Both use
        // original lyrics and an original cover painted in the album's palette.
        var preview = Compose(new Edition("preview", "The Adults Are Talking", "The Strokes", "The New Abnormal"));
        Save(preview, Path.Combine(_out, "01-main-screenshot-preview.png"));
        var publicShot = Compose(new Edition("public", "After the Rain", "Demo Artist", "Evening Studies"));
        Save(publicShot, Path.Combine(_out, "01-main-screenshot.png"));
        Save(Downscale(publicShot, 308, 173), Path.Combine(_out, "01-main-thumbnail.png"));
        Save(Downscale(publicShot, 616, 346), Path.Combine(_out, "raw-captures", "public", "thumbnail-2x.png"));
        Console.WriteLine($"Exported to {_out}");
    }

    static BitmapSource Compose(Edition edition)
    {
        var raw = Path.Combine(_out, "raw-captures", edition.Key);
        Directory.CreateDirectory(raw);
        var cover = Cover(640);
        Save(cover, Path.Combine(raw, "cover.png"));

        var monitor = new DemoMonitor(edition) { Thumbnail = cover };
        var settings = new SettingsService();
        var s = settings.Current;
        s.Language = "en";
        s.EnableAnimations = false;
        s.SpotifyLyricsEnabled = true; // shows the like button (its state is set below, offline)
        s.Background = OverlayBackground.AlbumGradient;
        s.TaskbarLyricFlow = LyricFlowMode.Automatic;
        s.Bar = new BarLayout
        {
            ShowCover = true, ShowText = true, ShowCaption = true, ShowPrevious = true, ShowPlayPause = true,
            ShowNext = true, ShowLike = true, ShowProgress = true, Width = 612,
        };
        s.MiniPlayer = BarLayout.DefaultMiniPlayer();
        s.MiniPlayerLook = new MiniPlayerStyle
        {
            Layout = MiniPlayerLayout.Cinema, Background = MiniPlayerBackground.BlurredCover, Highlight = LyricsHighlight.Expressive,
            Motion = LyricMotionMode.Expressive, Typeface = LyricsTypeface.Modern, Alignment = LyricsAlignment.Center,
            Controls = ControlsLook.Classic,
        };

        using var sync = new LyricsSyncService(monitor, new DemoLyrics(edition), settings);
        sync.Reload(); // completes synchronously: no network, no disk
        var likes = OfflineLikes(monitor, settings);

        // Taskbar player: 210 px tall in the composition, i.e. the 48 px bar at 4.375×.
        const double barScale = 210.0 / 48;
        using var barVm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService(), likes);
        var widget = RenderShown(new TaskbarOverlay(barVm), 2676 / barScale, 48, barScale);
        Save(widget, Path.Combine(raw, "taskbar-widget.png"));

        // Mini player, shown off-screen so its lyrics list lays out and centres like on the desktop.
        using var miniVm = new MiniPlayerViewModel(monitor, sync, settings, new AppVolumeService(), likes);
        // The Cinema card is 1.43:1 (not the 1.7:1 sketched): sized to clear the headline, right margin = browser left margin.
        var mini = RenderMini(miniVm, 1930);
        Save(mini.Bitmap, Path.Combine(raw, "mini-player.png"));
        Console.WriteLine($"{edition.Key}: mini card {mini.Card.Width:0} x {mini.Card.Height:0}");

        var browser = Browser(2466, 1398);
        Save(browser, Path.Combine(raw, "browser.png"));
        var taskbar = Taskbar(W, 306);
        Save(taskbar, Path.Combine(raw, "taskbar.png"));

        // Composition.
        var canvas = new Canvas { Width = W, Height = H };
        TextOptions.SetTextFormattingMode(canvas, TextFormattingMode.Ideal);
        Add(canvas, Backdrop(), 0, 0);

        Add(canvas, browser, 102, 102, shadow: new DropShadowEffect { BlurRadius = 90, ShadowDepth = 26, Direction = 270, Opacity = .32, Color = Petrol });

        // The mini player keeps its own soft shadow (its transparent margin); one deeper, wider shadow separates it from the browser.
        var miniCard = new Rect(W - 102 - mini.Card.Width, 112, mini.Card.Width, mini.Card.Height);
        canvas.Children.Add(Positioned(new Border
        {
            Width = miniCard.Width, Height = miniCard.Height, CornerRadius = new CornerRadius(12 * mini.Scale),
            Background = new SolidColorBrush(Petrol),
            Effect = new DropShadowEffect { BlurRadius = 160, ShadowDepth = 46, Direction = 270, Opacity = .55, Color = Hex("#08141C") },
        }, miniCard.X, miniCard.Y));
        Add(canvas, mini.Bitmap, miniCard.X - mini.Card.X, miniCard.Y - mini.Card.Y);

        Headline(canvas, "Less switching. More listening.", Path.Combine(_fonts, "InterDisplay-Bold.ttf"), 168, 1650, Cream, 1);
        Headline(canvas, "Your music, right on your taskbar.", Path.Combine(_fonts, "InterDisplay-Medium.ttf"), 84, 1776, Cream, .80);

        Add(canvas, taskbar, 0, H - 306);
        // A faint turquoise edge around the player: present, not a tutorial callout.
        var widgetRect = new Rect(66, 1902, widget.PixelWidth, widget.PixelHeight);
        // Around the player's pill (margin 2,4 and radius 6 in the bar), 10 px out.
        Add(canvas, widget, widgetRect.X, widgetRect.Y);
        var pill = new Rect(widgetRect.X + 2 * barScale - 10, widgetRect.Y + 4 * barScale - 10, widgetRect.Width - 4 * barScale + 20, widgetRect.Height - 8 * barScale + 20);
        canvas.Children.Add(Positioned(new Border
        {
            Width = pill.Width, Height = pill.Height, CornerRadius = new CornerRadius(6 * barScale + 10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, Turquoise.R, Turquoise.G, Turquoise.B)), BorderThickness = new Thickness(3),
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = .65, Color = Turquoise },
        }, pill.X, pill.Y));

        return Render(canvas, W, H);
    }

    // ---- Real app views -------------------------------------------------------------------------

    static SpotifyLikeService OfflineLikes(ISpotifyMonitor monitor, SettingsService settings)
    {
        // The like service talks to Spotify; here it only reports "liked", without being started.
        var likes = (SpotifyLikeService)RuntimeHelpers.GetUninitializedObject(typeof(SpotifyLikeService));
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(SpotifyLikeService).GetField("_monitor", flags)!.SetValue(likes, monitor);
        typeof(SpotifyLikeService).GetField("_settings", flags)!.SetValue(likes, settings);
        typeof(SpotifyLikeService).GetField("<IsLiked>k__BackingField", flags)!.SetValue(likes, (bool?)true);
        // A pending check that never fires: Refresh() then never reaches Spotify.
        var idle = new DispatcherTimer { Interval = TimeSpan.FromDays(1) };
        idle.Start();
        typeof(SpotifyLikeService).GetField("_checkTimer", flags)!.SetValue(likes, idle);
        return likes;
    }

    /// <summary>The taskbar player shown off-screen (it docks only when the app's service places it),
    /// so its lyric flow paints the way it does on the taskbar.</summary>
    static BitmapSource RenderShown(Window window, double width, double height, double scale)
    {
        window.Left = -20000; window.Top = -20000; window.Width = width; window.Height = height;
        window.ShowActivated = false; window.Topmost = false;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);
        window.Show();
        // On the taskbar it paints the taskbar's own colour; here, the composed taskbar's.
        window.Background = new SolidColorBrush(TaskbarColor);
        Pump();
        window.UpdateLayout();
        Pump();
        var bitmap = Render(window, (int)Math.Round(width * scale), (int)Math.Round(height * scale), scale, layout: false);
        window.Close();
        return bitmap;
    }

    record MiniRender(BitmapSource Bitmap, Rect Card, double Scale);

    static MiniRender RenderMini(MiniPlayerViewModel vm, double cardWidth)
    {
        var window = new MiniPlayerWindow(vm, () => false, () => true)
        {
            Left = -20000, Top = -20000, ShowActivated = false, Topmost = false,
        };
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);
        vm.IsActive = true;
        // What ShowAt does before showing, without placing the window over the taskbar.
        foreach (var step in new[] { "ApplyLayout", "ApplyControlsStyle", "UpdatePlayGlyph", "UpdateProgressValue", "ApplyBackground" })
            typeof(MiniPlayerWindow).GetMethod(step, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        window.Show();
        Pump();
        typeof(MiniPlayerWindow).GetMethod("ScrollToCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [false]);
        Pump();
        var chrome = (FrameworkElement)window.Content;
        double margin = chrome.Margin.Left; // the transparent shadow margin
        double cardW = window.ActualWidth - 2 * margin;
        double scale = cardWidth / cardW;
        var bitmap = Render(window, (int)Math.Round(window.ActualWidth * scale), (int)Math.Round(window.ActualHeight * scale), scale, layout: false);
        var card = new Rect(margin * scale, margin * scale, cardW * scale, (window.ActualHeight - 2 * margin) * scale);
        window.Close();
        vm.IsActive = false;
        return new MiniRender(bitmap, card, scale);
    }

    // ---- Scenery --------------------------------------------------------------------------------

    static BitmapSource Backdrop()
    {
        // Petrol blue dominating, opening into slate and a soft gold at the lower right.
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection
            {
                new(Hex("#0F2532"), 0), new(Petrol, .30), new(Hex("#1D4352"), .58), new(SlateBlue, .80), new(Hex("#8C8A66"), 1),
            }, new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, W, H));
            Glow(dc, new Point(2650, 760), 1500, 1100, Turquoise, 70);
            Glow(dc, new Point(3700, 1750), 1500, 1000, Gold, 150);
            Glow(dc, new Point(2300, 1650), 1300, 600, Gold, 40);
            Glow(dc, new Point(300, 300), 1500, 1100, Hex("#0A1B25"), 120);
        }
        var smooth = Render(v, W, H);
        // A grain of ±1 level: keeps the large gradients from banding, invisible as texture.
        var pixels = new byte[W * H * 4];
        smooth.CopyPixels(pixels, W * 4, 0);
        var random = new Random(42);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int d = random.Next(-1, 2);
            for (int c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Clamp(pixels[i + c] + d, 0, 255);
        }
        return BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, null, pixels, W * 4);
    }

    static void Glow(DrawingContext dc, Point centre, double rx, double ry, Color color, byte alpha) =>
        dc.DrawEllipse(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
            new(Color.FromArgb((byte)(alpha * .45), color.R, color.G, color.B), .5),
            new(Color.FromArgb(0, color.R, color.G, color.B), 1),
        }), null, centre, rx, ry);

    /// <summary>A light, deliberately plain browser: tabs, address bar and quiet content blocks.</summary>
    static BitmapSource Browser(int width, int height)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            var frame = new RectangleGeometry(new Rect(0, 0, width, height), 26, 26);
            dc.PushClip(frame);
            dc.DrawRectangle(B("#F6F7F5"), null, new Rect(0, 0, width, height));
            // Tab strip.
            dc.DrawRectangle(B("#E2E7E8"), null, new Rect(0, 0, width, 104));
            Tab(dc, 34, 520, active: true, line: 250);
            Tab(dc, 566, 440, active: false, line: 190);
            Tab(dc, 1018, 440, active: false, line: 230);
            Icon(dc, "", new Rect(1470, 26, 64, 64), 30, B("#7C878A"));
            // Window buttons (under the mini player, kept for realism).
            Icon(dc, "", new Rect(width - 330, 20, 90, 64), 26, B("#5D686B"));
            Icon(dc, "", new Rect(width - 220, 20, 90, 64), 26, B("#5D686B"));
            Icon(dc, "", new Rect(width - 110, 20, 90, 64), 26, B("#5D686B"));
            // Toolbar.
            dc.DrawRectangle(B("#FCFCFB"), null, new Rect(0, 104, width, 104));
            dc.DrawRectangle(B("#E4E8E8"), null, new Rect(0, 207, width, 2));
            Icon(dc, "", new Rect(30, 124, 64, 64), 30, B("#6C777A"));
            Icon(dc, "", new Rect(104, 124, 64, 64), 30, B("#B4BCBE"));
            Icon(dc, "", new Rect(178, 124, 64, 64), 30, B("#6C777A"));
            dc.DrawRoundedRectangle(B("#EEF1F1"), null, new Rect(270, 124, 1900, 64), 32, 32);
            Icon(dc, "", new Rect(296, 124, 64, 64), 24, B("#7C878A"));
            dc.DrawRoundedRectangle(B("#C9D0D2"), null, new Rect(372, 147, 420, 18), 9, 9);
            for (int i = 0; i < 3; i++)
                dc.DrawEllipse(B("#8D979A"), null, new Point(width - 90 + i * 0, 140 + i * 16), 5, 5);
            Icon(dc, "", new Rect(2200, 124, 64, 64), 28, B("#8D979A"));

            // Page: a quiet header, a lead block, a list of articles and a side column.
            double left = 150;
            dc.DrawRoundedRectangle(B("#DCE3E5"), null, new Rect(left, 290, 210, 36), 18, 18);
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(B("#E5EAEB"), null, new Rect(left + 300 + i * 170, 298, 130, 20), 10, 10);
            dc.DrawRoundedRectangle(B("#E6EBEC"), null, new Rect(left, 400, 1160, 300), 24, 24);
            dc.DrawRoundedRectangle(B("#CBD5D8"), null, new Rect(left + 56, 470, 520, 34), 17, 17);
            dc.DrawRoundedRectangle(B("#D7DFE1"), null, new Rect(left + 56, 530, 760, 22), 11, 11);
            dc.DrawRoundedRectangle(B("#D7DFE1"), null, new Rect(left + 56, 574, 640, 22), 11, 11);
            dc.DrawRoundedRectangle(B("#C4D3D6"), null, new Rect(left + 56, 626, 190, 44), 22, 22);
            for (int i = 0; i < 3; i++)
            {
                double y = 780 + i * 190;
                dc.DrawRoundedRectangle(B(i switch { 0 => "#C9D6D9", 1 => "#D6D3C6", _ => "#CBD3D8" }), null, new Rect(left, y, 230, 150), 18, 18);
                dc.DrawRoundedRectangle(B("#CED6D8"), null, new Rect(left + 280, y + 22, 600 - i * 70, 26), 13, 13);
                dc.DrawRoundedRectangle(B("#DFE5E6"), null, new Rect(left + 280, y + 70, 820, 18), 9, 9);
                dc.DrawRoundedRectangle(B("#DFE5E6"), null, new Rect(left + 280, y + 104, 700 - i * 40, 18), 9, 9);
            }
            for (int i = 0; i < 5; i++)
                dc.DrawRoundedRectangle(B("#E1E7E8"), null, new Rect(1420, 410 + i * 86, 760 - (i % 2) * 180, 22), 11, 11);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B("#C5CDCF"), 2), new Rect(1, 1, width - 2, height - 2), 26, 26);
        }
        return Render(v, width, height);
    }

    static void Tab(DrawingContext dc, double x, double width, bool active, double line)
    {
        var rect = new Rect(x, 22, width, 82);
        if (active)
        {
            var geometry = new PathGeometry();
            dc.DrawRoundedRectangle(B("#FCFCFB"), null, new Rect(x, 22, width, 100), 20, 20);
        }
        dc.DrawEllipse(B(active ? "#8FB4BC" : "#AEB8BA"), null, new Point(x + 52, rect.Top + 40), 16, 16);
        dc.DrawRoundedRectangle(B(active ? "#B9C3C5" : "#C3CBCD"), null, new Rect(x + 88, rect.Top + 31, line, 18), 9, 9);
        Icon(dc, "", new Rect(x + width - 70, rect.Top + 12, 56, 56), 20, B(active ? "#6C777A" : "#98A2A4"));
        if (!active)
            dc.DrawRectangle(B("#C9D0D2"), null, new Rect(x + width + 6, rect.Top + 22, 2, 38));
    }

    /// <summary>A Windows 11-style taskbar, scaled like the player it carries (4.375×): generic app
    /// icons (no other products' marks), the notification area and the clock.</summary>
    static BitmapSource Taskbar(int width, int height)
    {
        const double k = 210.0 / 48;
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(TaskbarColor), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), null, new Rect(0, 0, width, 2));
            double mid = height / 2.0;
            // App icons right after the player, as when the taskbar is left-aligned.
            (string glyph, string tint)[] apps = [("", "#E8C468"), ("", "#5FC4D8"), ("", "#8FB2E8")];
            double x = 2800, cell = 36 * k;
            for (int i = 0; i < apps.Length; i++)
            {
                var r = new Rect(x + i * cell, mid - cell / 2 + 4, cell, cell);
                if (i == 1)
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), null, Rect.Inflate(r, -2 * k, -2 * k), 5 * k, 5 * k);
                Icon(dc, apps[i].glyph, r, 17 * k, B(apps[i].tint));
                if (i is 1 or 2)
                    dc.DrawRoundedRectangle(i == 1 ? B("#5FC4D8") : B("#8C989D"), null,
                        new Rect(r.Left + r.Width / 2 - (i == 1 ? 8 : 3) * k, r.Bottom - 5 * k, (i == 1 ? 16 : 6) * k, 3 * k), 1.5 * k, 1.5 * k);
            }
            // Notification area and clock.
            var ink = B("#EEF2F0");
            double right = width - 14 * k;
            var font = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var time = new FormattedText("10:42 AM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10.5 * k, ink, 1);
            var date = new FormattedText("11/27/2026", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10.5 * k, ink, 1);
            double clock = Math.Max(time.Width, date.Width);
            dc.DrawText(time, new Point(right - (clock + time.Width) / 2, mid - time.Height + 2));
            dc.DrawText(date, new Point(right - (clock + date.Width) / 2, mid + 2));
            double tray = right - clock - 12 * k;
            foreach (var glyph in new[] { "", "" })
            {
                tray -= 20 * k;
                Icon(dc, glyph, new Rect(tray, 0, 20 * k, height), 13 * k, ink);
            }
        }
        return Render(v, width, height);
    }

    static void Headline(Canvas canvas, string text, string fontFile, double size, double baseline, Color color, double opacity)
    {
        var family = new FontFamily(new Uri(Path.GetDirectoryName(fontFile)! + Path.DirectorySeparatorChar), "./#Inter Display");
        var weight = fontFile.Contains("Bold") ? FontWeights.Bold : FontWeights.Medium;
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("en"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.White, 1);
        var block = new TextBlock
        {
            Text = text, FontFamily = family, FontWeight = weight, FontSize = size, Opacity = opacity,
            Foreground = new SolidColorBrush(color),
            Effect = new DropShadowEffect { BlurRadius = size * .5, ShadowDepth = size * .04, Direction = 270, Opacity = .28, Color = Hex("#07131A") },
        };
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(block, TextRenderingMode.Grayscale);
        canvas.Children.Add(Positioned(block, (W - formatted.Width) / 2, baseline - formatted.Baseline));
    }

    /// <summary>Original cover art in the album's palette: layered colour fields and painted strokes.</summary>
    static BitmapSource Cover(int size)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 300.0, size / 300.0));
            dc.DrawRectangle(new LinearGradientBrush(Turquoise, Hex("#1F7F95"), 60), null, new Rect(0, 0, 300, 300));
            Fill(dc, "#E2C46F", "M0,0 L132,0 L104,96 L0,118 Z");
            Fill(dc, "#142D3C", "M170,0 L300,0 L300,140 L226,118 Z");
            Fill(dc, "#356B79", "M0,300 L0,196 L120,170 L176,300 Z");
            Fill(dc, "#F1F2ED", "M214,300 L246,210 L300,198 L300,300 Z");
            Fill(dc, "#AF9B67", "M118,168 L196,150 L176,226 Z");
            var dark = new Pen(B("#122733"), 15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var cream = new Pen(B("#F1F2ED"), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(dark, new Point(64, 34), new Point(98, 262));
            dc.DrawLine(dark, new Point(150, 26), new Point(132, 250));
            dc.DrawLine(dark, new Point(212, 60), new Point(248, 238));
            dc.DrawLine(dark, new Point(98, 262), new Point(176, 226));
            dc.DrawLine(cream, new Point(24, 128), new Point(276, 72));
            dc.DrawLine(cream, new Point(40, 230), new Point(262, 146));
            dc.DrawLine(cream, new Point(186, 40), new Point(270, 270));
            dc.Pop();
        }
        return Render(v, size, size);
    }

    static void Fill(DrawingContext dc, string color, string path) => dc.DrawGeometry(B(color), null, Geometry.Parse(path));

    // ---- Helpers --------------------------------------------------------------------------------

    static void Add(Canvas canvas, BitmapSource bitmap, double x, double y, Effect? shadow = null)
    {
        var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.Fill, Effect = shadow };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        canvas.Children.Add(Positioned(image, x, y));
    }

    static T Positioned<T>(T element, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }

    static void Icon(DrawingContext dc, string glyph, Rect cell, double size, Brush brush)
    {
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, 1);
        dc.DrawText(text, new Point(cell.Left + (cell.Width - text.Width) / 2, cell.Top + (cell.Height - text.Height) / 2));
    }

    static BitmapSource Render(Visual visual, int width, int height, double scale = 1, bool layout = true)
    {
        if (layout && visual is UIElement element)
        {
            element.Measure(new Size(width / scale, height / scale));
            element.Arrange(new Rect(0, 0, width / scale, height / scale));
            element.UpdateLayout();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Halves repeatedly, then a final high-quality step: crisp small thumbnails.</summary>
    static BitmapSource Downscale(BitmapSource source, int width, int height)
    {
        var current = source;
        while (current.PixelWidth / 2 >= width * 1.5)
            current = Resize(current, current.PixelWidth / 2, current.PixelHeight / 2);
        return Resize(current, width, height);
    }

    static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return Render(image, width, height);
    }

    static void Pump()
    {
        for (int i = 0; i < 4; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static SolidColorBrush B(string hex) => new(Hex(hex));
}

/// <summary>Original demonstration lyrics, timed so one line is mid-way when the frame is taken.</summary>
internal sealed record Edition(string Key, string Title, string Artist, string Album);

internal sealed class DemoLyrics(Edition edition) : ILyricsService
{
    public static readonly (double At, string Text)[] Lines =
    [
        (0, "City lights are fading slow"),
        (7, "Somewhere in time"),
        (13, "Every little moment"),
        (19, "Some moments stay with us."),
        (26, "Until we meet again"),
        (32, "Keep the music playing"),
        (38, "All the way back home"),
    ];

    public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken ct) => Task.FromResult(new LyricsResult(LyricsKind.Synced,
        Lines.Select(l => new LyricsLine(TimeSpan.FromSeconds(l.At), l.Text)).ToArray(), null, query.Title, query.Artist));

    public Task ForgetAsync(TrackQuery query) => Task.CompletedTask;
}

internal sealed class DemoMonitor(Edition edition) : ISpotifyMonitor
{
    public bool HasSession => true;
    public string? SessionAppId => "Spotify.exe";
    public IReadOnlyList<string> AllSessionAppIds => ["Spotify.exe"];
    public TrackInfo? Track => new(edition.Title, edition.Artist, edition.Album);
    public PlaybackState Status { get; set; } = PlaybackState.Playing;
    // Mid-way through "Some moments stay with us." (19 s to 26 s).
    public TimeSpan Position => TimeSpan.FromSeconds(22.4);
    public TimeSpan? Duration => TimeSpan.FromSeconds(31);
    public ImageSource? Thumbnail { get; set; }
    public bool CanSeek => true; public bool CanGoNext => true; public bool CanGoPrevious => true;
    public bool CanShuffle => true; public bool CanRepeat => true; public bool IsShuffleActive => false;
    public RepeatMode RepeatMode => RepeatMode.None;
    public (Color Start, Color End) Palette => (Color.FromRgb(0x14, 0x2D, 0x3C), Color.FromRgb(0x2B, 0x6F, 0x80));
    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged;
    public Task PlayPauseAsync() => Task.CompletedTask; public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask; public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public Task ToggleShuffleAsync() => Task.CompletedTask; public Task CycleRepeatAsync() => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public void Dispose() { }
}
