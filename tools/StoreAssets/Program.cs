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
        using var miniVm = new MiniPlayerViewModel(monitor, sync);
        var miniWindow = new MiniPlayerWindow(miniVm, () => false, () => true);
        miniVm.IsActive = true;
        typeof(MiniPlayerWindow).GetMethod("ApplyBackground", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(miniWindow, null);
        var mini = RenderWindow(miniWindow, 384, double.NaN, 2);
        var settingsVm = new SettingsViewModel(settings, new LyricsCache(Path.Combine(output, "unused-cache")), sync, false, _ => {});
        var settingsWindow = new SettingsWindow(settingsVm);
        var settingsTop = RenderWindow(settingsWindow, 560, 770, 2, settingsFrame: true);
        var customizationWindow = new SettingsWindow(settingsVm);
        var scroll = (ScrollViewer)customizationWindow.Content;
        scroll.Measure(new Size(560,770)); scroll.Arrange(new Rect(0,0,560,770)); scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(355);
        var customization = RenderWindow(customizationWindow, 560, 770, 2, settingsFrame: true);

        Card("01-taskbar-lyrics.png", "YOUR MUSIC. WITHIN SIGHT.", "Lyrics, right on\nyour taskbar.",
            "Follow the song without switching windows.\nSynced lyrics for Spotify on Windows 11.", dc =>
        {
            Text(dc, "TASKBAR PLAYER", 152, 620, 18, Green);
            Panel(dc, new Rect(148, 674, 1624, 182));
            dc.DrawImage(bar, new Rect(218, 703, 1485, 130));
        });
        Card("02-mini-player.png", "ONE CLICK CLOSER", "A little player.\nMore of your music.",
            "Album artwork, scrolling lyrics and playback\ncontrols in one compact window.", dc =>
        {
            Panel(dc, new Rect(1060, 190, 650, 700));
            dc.DrawImage(mini, new Rect(1100, 230, 570, 570 * mini.PixelHeight / mini.PixelWidth));
            Text(dc, "Read along", 152, 690, 28, Ink);
            Text(dc, "Click a lyric line to jump to that moment.", 152, 740, 23, Muted);
        }, narrow: true);
        Card("03-customize-player.png", "BUILT AROUND YOU", "Your taskbar.\nYour choices.",
            "Choose your controls, colors and layout.\nKeep a compact player visible when paused.", dc =>
        {
            dc.DrawImage(customization, new Rect(1060, 130, 616, 847));
            Text(dc, "PLAYING", 152, 665, 18, Green);
            dc.DrawImage(bar, new Rect(152, 705, 770, 67));
            monitor.Status = PlaybackState.Paused;
            using var pausedVm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService());
            var paused = RenderWindow(new TaskbarOverlay(pausedVm), 240, 48, 3);
            Text(dc, "PAUSED", 152, 810, 18, Green);
            dc.DrawImage(paused, new Rect(152, 850, 336, 67));
        }, narrow: true);
        Card("04-language-settings.png", "FEEL AT HOME", "Six languages.\nOne simple setting.",
            "English, Portuguese, Spanish, French,\nGerman and Italian. Switch in Settings.", dc =>
        {
            dc.DrawImage(settingsTop, new Rect(1060, 130, 616, 847));
            Text(dc, "English  /  Português  /  Español", 152, 710, 27, Ink);
            Text(dc, "Français  /  Deutsch  /  Italiano", 152, 765, 27, Ink);
        }, narrow: true);

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

    static void Card(string filename,string kicker,string headline,string description,Action<DrawingContext> content,bool narrow=false)
    {
        var visual = new DrawingVisual();
        using (var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(7,18,17),Color.FromRgb(13,29,25),35),null,new Rect(0,0,1920,1080));
            dc.DrawRoundedRectangle(null,new Pen(Brush("#203A30"),1),new Rect(64,64,1792,952),28,28);
            dc.DrawImage(logo,new Rect(146,108,48,48));
            Text(dc,"Taskitify",210,110,30,Ink);
            Text(dc,kicker,152,230,19,Green);
            Text(dc,headline,146,290,narrow ? 66 : 83,Ink);
            Text(dc,description,152,narrow ? 490 : 510,27,Muted);
            content(dc);
            Text(dc,"Actual Taskitify interface · Demonstration track and lyrics",152,976,16,Muted);
        }
        Save(visual,1920,1080,filename);
    }
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
