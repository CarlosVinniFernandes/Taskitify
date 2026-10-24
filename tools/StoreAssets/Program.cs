using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;
using Taskitify.Views;

// Offline export of the production WPF views. Never loads or saves user settings,
// accesses media sessions, starts playback, or sends requests to lyrics providers.
internal static class Program
{
    static string output = "";
    static BitmapImage logo = null!;
    static readonly Brush Ink = Brush("#F3F7F5");
    static readonly Brush Muted = Brush("#9DADA8");
    static readonly Brush Green = Brush("#42F58D");

    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        if (args.Contains("--logo")) { ExportLogo(root); return; }
        output = Path.Combine(root, "dist", "store-assets", "en");
        Directory.CreateDirectory(output);
        logo = new BitmapImage(new Uri(Path.Combine(root, "docs", "images", "logo.png")));
        var app = new Taskitify.App();
        app.InitializeComponent();
        using var theme = new ThemeService();
        LocalizationService.SetLanguage("en");
        var monitor = new DemoMonitor { Thumbnail = DemoCover() };
        var settings = new SettingsService();
        settings.Current.EnableAnimations = false;
        settings.Current.PausedBehavior = PausedBehavior.Minimize;
        settings.Current.Bar.ShowPrevious = true;
        settings.Current.Bar.ShowPlayPause = true;
        settings.Current.Bar.ShowNext = true;
        settings.Current.Bar.ShowProgress = true;
        using var sync = new LyricsSyncService(monitor, new DemoLyrics(), settings);
        sync.Reload(); // demo service completes synchronously, without network or disk access
        using var barVm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService());
        var bar = RenderWindow(new TaskbarOverlay(barVm), 550, 48, 3);
        using var miniVm = new MiniPlayerViewModel(monitor, sync, settings, new AppVolumeService());
        var miniWindow = new MiniPlayerWindow(miniVm, () => false, () => true);
        miniVm.IsActive = true;
        typeof(MiniPlayerWindow).GetMethod("ApplyBackground", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(miniWindow, null);
        var mini = RenderWindow(miniWindow, 384, double.NaN, 2);
        var settingsVm = new SettingsViewModel(settings, new LyricsCache(Path.Combine(output, "unused-cache")), sync, false, _ => {});
        // Only shows its status: the web player never starts unless asked to connect.
        using var spotify = new SpotifyLyricsSession(settings, app.Dispatcher, Path.Combine(output, "unused-browser"));
        var settingsWindow = new SettingsWindow(settingsVm, spotify);
        var settingsTop = RenderWindow(settingsWindow, 560, 770, 2, settingsFrame: true);
        var customizationWindow = new SettingsWindow(settingsVm, spotify);
        var scroll = (ScrollViewer)customizationWindow.Content;
        scroll.Measure(new Size(560,770)); scroll.Arrange(new Rect(0,0,560,770)); scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(355);
        var customization = RenderWindow(customizationWindow, 560, 770, 2, settingsFrame: true);

        // Listing screenshots: the player as it sits on a Windows 11 taskbar, in the weather widget's place.
        var onTaskbar = RenderWindow(new TaskbarOverlay(barVm), 420, 48, 3);
        Scene("01-taskbar-lyrics.png", 152, "YOUR MUSIC. WITHIN SIGHT.", "Lyrics, right on\nyour taskbar.",
            "Follow the song without switching windows.\nSynced lyrics for Spotify on Windows 11.",
            onTaskbar, 420, "Synced lyrics, where the weather widget sits");
        Scene("02-mini-player.png", 900, "ONE CLICK CLOSER", "A little player.\nMore of your music.",
            "Click the lyrics on your taskbar to open album\nartwork, scrolling lyrics and playback controls.",
            onTaskbar, 420, null, (dc, top) =>
            {
                double width = 384 * SceneScale, height = width * mini.PixelHeight / mini.PixelWidth;
                Shadowed(dc, new Rect(8 * SceneScale, top - 12 - height, width, height), 14);
                dc.DrawImage(mini, new Rect(8 * SceneScale, top - 12 - height, width, height));
            });
        monitor.Status = PlaybackState.Paused;
        using var pausedVm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService());
        var paused = RenderWindow(new TaskbarOverlay(pausedVm), 240, 48, 3);
        Scene("03-customize-player.png", 152, "BUILT AROUND YOU", "Your taskbar.\nYour choices.",
            "Choose your controls, colors and layout.\nKeep a compact player visible when paused.",
            paused, 240, "A compact player while the music is paused", (dc, top) => SettingsOnDesktop(dc, customization, top));
        Scene("04-language-settings.png", 152, "FEEL AT HOME", "Six languages.\nOne simple setting.",
            "English, Portuguese, Spanish, French,\nGerman and Italian. Switch in Settings.",
            onTaskbar, 420, null, (dc, top) => SettingsOnDesktop(dc, settingsTop, top));

        foreach (int size in new[] { 71, 150, 300, 1080, 2160 })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brush("#06130F"), null, new Rect(0,0,size,size));
                dc.DrawImage(logo, new Rect(size*.08,size*.08,size*.84,size*.84));
            }
            Save(visual, size, size, $"logo-{size}x{size}.png");
        }
        foreach (int scale in new[] { 1, 2 })
        {
            var poster = new DrawingVisual();
            using (var dc = poster.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(13,29,25),Color.FromRgb(6,19,15),90),null,new Rect(0,0,720,1080));
                dc.DrawImage(logo,new Rect(150,150,420,420));
                CenteredText(dc,"Taskitify",620,88,Ink);
                CenteredText(dc,"Synced lyrics on your taskbar.",740,32,Muted);
                Panel(dc,new Rect(40,850,640,96));
                dc.DrawImage(bar,new Rect(60,872,600,600.0*bar.PixelHeight/bar.PixelWidth));
                dc.Pop();
            }
            Save(poster,720*scale,1080*scale,$"poster-{720*scale}x{1080*scale}.png");
        }
        double miniRatio = (double)mini.PixelHeight / mini.PixelWidth;
        double barRatio = (double)bar.PixelHeight / bar.PixelWidth;
        // 16:9 super hero art: no product title allowed, the Store overlays its own text at the bottom left.
        foreach (int scale in new[] { 1, 2 })
            Art($"superhero-{1920*scale}x{1080*scale}.png", 1920, 1080, scale, dc =>
            {
                Glow(dc, new Point(1400, 420), 760);
                double miniW = 470;
                Panel(dc, new Rect(1150, 100, miniW + 60, miniW * miniRatio + 60));
                dc.DrawImage(mini, new Rect(1180, 130, miniW, miniW * miniRatio));
                Panel(dc, new Rect(160, 300, 880, 140));
                dc.DrawImage(bar, new Rect(190, 370 - 820 * barRatio / 2, 820, 820 * barRatio));
            });
        // Xbox titled hero art: title within the top three quarters.
        Art("hero-titled-1920x1080.png", 1920, 1080, 1, dc =>
        {
            Glow(dc, new Point(1400, 420), 760);
            dc.DrawImage(logo, new Rect(160, 170, 150, 150));
            Text(dc, "Taskitify", 150, 340, 120, Ink);
            Text(dc, "Synced lyrics on your taskbar.", 160, 520, 40, Muted);
            Panel(dc, new Rect(160, 610, 820, 120));
            dc.DrawImage(bar, new Rect(185, 670 - 770 * barRatio / 2, 770, 770 * barRatio));
            double miniW = 430;
            Panel(dc, new Rect(1190, 90, miniW + 60, miniW * miniRatio + 60));
            dc.DrawImage(mini, new Rect(1220, 120, miniW, miniW * miniRatio));
        });
        // Xbox branded key art: title within the top three quarters.
        Art("keyart-584x800.png", 584, 800, 1, dc =>
        {
            Glow(dc, new Point(292, 250), 340);
            dc.DrawImage(logo, new Rect(142, 70, 300, 300));
            CenteredText(dc, "Taskitify", 400, 70, Ink, 584);
            CenteredText(dc, "Synced lyrics on your taskbar.", 495, 25, Muted, 584);
        });
        // Featured promotional square: no product title.
        Art("promo-square-1080x1080.png", 1080, 1080, 1, dc =>
        {
            double miniW = 420;
            double miniH = miniW * miniRatio;
            double top = (1080 - (miniH + 60 + 60 + 110)) / 2;
            Glow(dc, new Point(540, top + miniH / 2 + 30), 560);
            Panel(dc, new Rect(540 - miniW / 2 - 30, top, miniW + 60, miniH + 60));
            dc.DrawImage(mini, new Rect(540 - miniW / 2, top + 30, miniW, miniH));
            double barTop = top + miniH + 120;
            Panel(dc, new Rect(110, barTop, 860, 110));
            dc.DrawImage(bar, new Rect(135, barTop + 55 - 810 * barRatio / 2, 810, 810 * barRatio));
        });
        miniVm.IsActive = false;
        Console.WriteLine($"Store assets exported to {output}");
    }

    static BitmapSource RenderWindow(Window window, double width, double height, double scale, bool settingsFrame = false)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = window.DataContext;
        content.Resources.MergedDictionaries.Add(window.Resources);
        var container = new Grid();
        System.Windows.Documents.TextElement.SetForeground(container, window.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(container, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(container, window.FontSize);
        container.Children.Add(content);
        FrameworkElement visual = container;
        if (settingsFrame)
        {
            var dock = new DockPanel();
            var title = new TextBlock { Text = "Taskitify — Settings", FontSize = 13, Foreground = Ink, Margin = new Thickness(24,15,24,15) };
            container.Children.Clear();
            DockPanel.SetDock(title,Dock.Top); dock.Children.Add(title); dock.Children.Add(content);
            visual = new Border { Background = window.Background, CornerRadius = new CornerRadius(12), Child = dock,
                BorderBrush = Brush("#405047"), BorderThickness = new Thickness(1) };
            System.Windows.Documents.TextElement.SetForeground(visual, window.Foreground);
            System.Windows.Documents.TextElement.SetFontFamily(visual, window.FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(visual, window.FontSize);
        }
        if (double.IsNaN(height)) { visual.Measure(new Size(width,double.PositiveInfinity)); height = Math.Ceiling(visual.DesiredSize.Height); }
        visual.Width=width; visual.Height=height;
        visual.Measure(new Size(width,height));visual.Arrange(new Rect(0,0,width,height));visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }

    // Screen pixels per taskbar pixel: a 1200-pixel-wide desktop, so the taskbar reads at listing size.
    const double SceneScale = 1.6;

    /// <summary>A desktop with a Windows 11-style taskbar carrying the player. No Start button or
    /// other products' icons: the Store rejects other products' marks in listing images.</summary>
    static void Scene(string filename, double textLeft, string kicker, string headline, string description,
        BitmapSource player, double playerWidth, string? callout, Action<DrawingContext, double>? desktop = null)
    {
        const double width = 1920, height = 1080, s = SceneScale;
        double top = height - 48 * s;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(8, 24, 21), Color.FromRgb(16, 58, 48), 40), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(70, 66, 245, 141), Color.FromArgb(0, 66, 245, 141)), null, new Point(1500, 260), 760, 620);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(55, 40, 170, 200), Color.FromArgb(0, 40, 170, 200)), null, new Point(260, 860), 700, 520);
            dc.DrawImage(logo, new Rect(textLeft - 6, 96, 48, 48));
            Text(dc, "Taskitify", textLeft + 58, 98, 30, Ink);
            Text(dc, kicker, textLeft, 210, 19, Green);
            Text(dc, headline, textLeft - 6, 262, 76, Ink);
            Text(dc, description, textLeft, 486, 27, Muted);
            desktop?.Invoke(dc, top);

            // Taskbar: the player at the left edge, generic app icons centered, then the tray and clock.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(235, 28, 32, 31)), null, new Rect(0, top, width, height - top));
            dc.DrawRectangle(Brush("#3A3F3D"), null, new Rect(0, top, width, 1));
            var playerRect = new Rect(8 * s, top, playerWidth * s, 48 * s);
            dc.DrawImage(player, playerRect);
            string[] apps = ["", "", "", "", "", ""];
            double appsLeft = width / 2 - apps.Length * 44 * s / 2;
            for (int i = 0; i < apps.Length; i++)
            {
                var cell = new Rect(appsLeft + i * 44 * s + 2 * s, top + 4 * s, 40 * s, 40 * s);
                if (i == 1) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, cell, 5 * s, 5 * s);
                Glyph(dc, apps[i], cell, 20 * s, Ink);
                if (i is 1 or 2) dc.DrawRoundedRectangle(i == 1 ? Green : Muted, null,
                    new Rect(cell.Left + cell.Width / 2 - (i == 1 ? 8 : 3) * s, cell.Bottom - 3 * s, (i == 1 ? 16 : 6) * s, 3 * s), 1.5 * s, 1.5 * s);
            }
            double right = width - 12 * s;
            var date = Formatted("10/23/2026", 12 * s, Ink);
            var time = Formatted("9:41 PM", 12 * s, Ink);
            double clock = Math.Max(date.Width, time.Width);
            dc.DrawText(time, new Point(right - time.Width, top + 8 * s));
            dc.DrawText(date, new Point(right - date.Width, top + 25 * s));
            double tray = right - clock - 16 * s;
            foreach (var glyph in new[] { "", "", "" })
            {
                tray -= 22 * s;
                Glyph(dc, glyph, new Rect(tray, top, 22 * s, 48 * s), 14 * s, Ink);
            }
            Glyph(dc, "", new Rect(tray - 30 * s, top, 24 * s, 48 * s), 12 * s, Ink);

            if (callout is not null)
            {
                // Kept inside the image: the taskbar ends at its bottom edge.
                var outline = new Rect(playerRect.Left - 6, playerRect.Top - 6, playerRect.Width + 12, playerRect.Height + 2);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 66, 245, 141)), 10), outline, 14, 14);
                dc.DrawRoundedRectangle(null, new Pen(Green, 3), outline, 14, 14);
                var label = Formatted(callout, 22, Brush("#06130F"), FontWeights.SemiBold);
                var pill = new Rect(outline.Left, outline.Top - 30 - label.Height - 24, label.Width + 40, label.Height + 24);
                dc.DrawRoundedRectangle(Green, null, pill, pill.Height / 2, pill.Height / 2);
                dc.DrawText(label, new Point(pill.Left + 20, pill.Top + 12));
                dc.DrawLine(new Pen(Green, 3), new Point(pill.Left + 40, pill.Bottom), new Point(pill.Left + 40, outline.Top));
            }
            Text(dc, "Actual Taskitify interface · Demonstration track and lyrics", textLeft, 590, 16, Muted);
        }
        Save(visual, (int)width, (int)height, filename);
    }

    static void SettingsOnDesktop(DrawingContext dc, BitmapSource settingsWindow, double taskbarTop)
    {
        double height = taskbarTop - 70, width = height * settingsWindow.PixelWidth / settingsWindow.PixelHeight;
        var rect = new Rect(1880 - width, 40, width, height);
        Shadowed(dc, rect, 12);
        dc.DrawImage(settingsWindow, rect);
    }

    static void Shadowed(DrawingContext dc, Rect rect, double radius)
    {
        for (int i = 1; i <= 6; i++)
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)), null, Rect.Inflate(new Rect(rect.X, rect.Y + 8, rect.Width, rect.Height), i * 4, i * 4), radius + i * 4, radius + i * 4);
    }

    static void Glyph(DrawingContext dc, string glyph, Rect cell, double size, Brush brush)
    {
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, 1);
        dc.DrawText(text, new Point(cell.Left + (cell.Width - text.Width) / 2, cell.Top + (cell.Height - text.Height) / 2));
    }

    static FormattedText Formatted(string text, double size, Brush brush, FontWeight? weight = null) =>
        new(text, CultureInfo.GetCultureInfo("en"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, brush, 1);
    static void Panel(DrawingContext dc,Rect rect) => dc.DrawRoundedRectangle(Brush("#10251C"),new Pen(Brush("#294B38"),1),rect,24,24);
    static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    static void Text(DrawingContext dc,string text,double x,double y,double size,Brush brush) =>
        dc.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("en"),FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,size>40 ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal),size,brush,1),new Point(x,y));
    static void CenteredText(DrawingContext dc,string text,double y,double size,Brush brush,double width=720)
    {
        var formatted = new FormattedText(text,CultureInfo.GetCultureInfo("en"),FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,size>40 ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal),size,brush,1);
        dc.DrawText(formatted,new Point((width-formatted.Width)/2,y));
    }
    static void Art(string filename,int width,int height,int scale,Action<DrawingContext> content)
    {
        var visual = new DrawingVisual();
        using (var dc=visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale,scale));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(13,29,25),Color.FromRgb(6,19,15),60),null,new Rect(0,0,width,height));
            content(dc);
            dc.Pop();
        }
        Save(visual,width*scale,height*scale,filename);
    }
    static void Glow(DrawingContext dc,Point center,double radius)
    {
        var brush = new RadialGradientBrush(Color.FromArgb(70,66,245,141),Color.FromArgb(0,66,245,141));
        dc.DrawEllipse(brush,null,center,radius,radius);
    }
    static void Save(Visual visual,int width,int height,string filename)
    {
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(output,filename));encoder.Save(file);
    }
    /// <summary>The app logo as vectors, on a 256 × 256 canvas. Only Taskitify's own artwork:
    /// the Store rejects other products' marks (Spotify, Windows) in icons and artwork.</summary>
    static Drawing LogoDrawing()
    {
        var green = Color.FromRgb(0x42, 0xF5, 0x8D);
        Brush Alpha(byte a) => new SolidColorBrush(Color.FromArgb(a, green.R, green.G, green.B));
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var center = new Point(128, 128);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0x10, 0x2A, 0x20), Color.FromRgb(0x04, 0x0C, 0x09)), null, center, 122, 122);
            dc.DrawEllipse(null, new Pen(Alpha(60), 20), center, 112, 112);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(green), 9), center, 112, 112);
            // The taskbar player: album art, two lyric lines and a waveform.
            dc.DrawRoundedRectangle(Brush("#0C2018"), new Pen(Alpha(150), 3.5), new Rect(38, 92, 180, 72), 36, 36);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x1E, 0x6B, 0x4A), green, 45), null, new Rect(58, 106, 44, 44), 10, 10);
            var note = Brush("#06130F");
            dc.DrawEllipse(note, null, new Point(75, 138), 7, 5.5);
            dc.DrawRectangle(note, null, new Rect(79.5, 115, 3.5, 23));
            dc.DrawRoundedRectangle(note, null, new Rect(79.5, 115, 12, 6), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(green), null, new Rect(112, 113, 50, 11), 5.5, 5.5);
            dc.DrawRoundedRectangle(Alpha(140), null, new Rect(112, 132, 34, 9), 4.5, 4.5);
            double[] bars = [12, 26, 38, 22, 12];
            for (int i = 0; i < bars.Length; i++)
                dc.DrawRoundedRectangle(new SolidColorBrush(green), null, new Rect(170 + i * 8, 128 - bars[i] / 2, 5, bars[i]), 2.5, 2.5);
        }
        drawing.Freeze();
        return drawing;
    }

    static byte[] LogoPng(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            dc.DrawDrawing(LogoDrawing());
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    /// <summary>docs/images/logo.png and the app icon, with PNG frames from 16 to 256 pixels.</summary>
    static void ExportLogo(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "docs", "images", "logo.png"), LogoPng(512));
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = sizes.Select(LogoPng).ToArray();
        using var icon = new BinaryWriter(File.Create(Path.Combine(root, "src", "Taskitify", "Resources", "Taskitify.ico")));
        icon.Write((short)0); icon.Write((short)1); icon.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            icon.Write((byte)(sizes[i] % 256)); icon.Write((byte)(sizes[i] % 256));
            icon.Write((byte)0); icon.Write((byte)0); icon.Write((short)1); icon.Write((short)32);
            icon.Write(frames[i].Length); icon.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) icon.Write(frame);
        Console.WriteLine("Logo exported to docs/images/logo.png and src/Taskitify/Resources/Taskitify.ico");
    }

    static ImageSource DemoCover()
    {
        var drawing=new DrawingGroup();
        using(var dc=drawing.Open())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(14,45,48),Color.FromRgb(48,143,114),45),null,new Rect(0,0,300,300));
            dc.DrawEllipse(Brush("#E1F8C5"),null,new Point(190,100),48,48);
            for(int i=0;i<9;i++) dc.DrawLine(new Pen(Brush("#1B645C"),9),new Point(0,180+i*15),new Point(300,125+i*15));
            Text(dc,"AFTERGLOW",24,236,28,Ink);
        }
        return new DrawingImage(drawing);
    }
}

internal sealed class DemoLyrics : ILyricsService
{
    public Task<LyricsResult> GetLyricsAsync(TrackQuery query,CancellationToken ct) => Task.FromResult(new LyricsResult(LyricsKind.Synced,
        new[] { "The city settles into blue", "A quiet rhythm carries through", "We leave the noise beyond the door", "And find the light we're looking for", "Let the evening take its time", "One small moment, yours and mine", "The last light follows where we go", "We keep a little afterglow" }
        .Select((text,i)=>new LyricsLine(TimeSpan.FromSeconds(i*20),text)).ToArray(),null,"Afterglow","Taskitify Demo"));
    public Task ForgetAsync(TrackQuery query)=>Task.CompletedTask;
}

internal sealed class DemoMonitor : ISpotifyMonitor
{
    public bool HasSession=>true;
    public string? SessionAppId=>"Spotify.exe";
    public IReadOnlyList<string> AllSessionAppIds=>["Spotify.exe"];
    public TrackInfo? Track=>new("Afterglow","Taskitify Demo","Evening Studies");
    public PlaybackState Status { get; set; }=PlaybackState.Playing;
    public TimeSpan Position=>TimeSpan.FromSeconds(63);
    public TimeSpan? Duration=>TimeSpan.FromSeconds(208);
    public ImageSource? Thumbnail {get;set;}
    public bool CanSeek=>true; public bool CanGoNext=>true; public bool CanGoPrevious=>true;
    public bool CanShuffle=>true;public bool CanRepeat=>true;public bool IsShuffleActive=>false;
    public RepeatMode RepeatMode=>RepeatMode.None;
    public (Color Start,Color End) Palette=>(Color.FromRgb(15,45,39),Color.FromRgb(33,98,74));
    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged;
    public Task PlayPauseAsync()=>Task.CompletedTask; public Task NextAsync()=>Task.CompletedTask;
    public Task PreviousAsync()=>Task.CompletedTask;public Task SeekAsync(TimeSpan position)=>Task.CompletedTask;
    public Task ToggleShuffleAsync()=>Task.CompletedTask;public Task CycleRepeatAsync()=>Task.CompletedTask;
    public Task StartAsync()=>Task.CompletedTask;
    public void Dispose() {}
}
