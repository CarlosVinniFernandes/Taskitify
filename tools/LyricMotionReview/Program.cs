using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Layout;
using Taskitify.Core.Lyrics;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.ViewModels;
using Taskitify.Views;

namespace TaskitifyReview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Taskitify;component/Resources/Styles.xaml") });
        app.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        app.Resources["NullToCollapsed"] = new Taskitify.Infrastructure.NullToCollapsedConverter();
        app.Startup += async (_, _) =>
        {
            try { await Run(args); app.Shutdown(); }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); }
        };
        return app.Run();
    }

    internal static async Task Run(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Expected smoke|demo|bench and output directory.");
        var command = args[0];
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("TASKITIFY_DATA_DIR", Path.Combine(output, "data-" + Environment.ProcessId));
        LocalizationService.SetLanguage("en");
        var settings = new SettingsService();
        settings.Load();
        var cover = args.Length > 2 && File.Exists(args[2]) ? new BitmapImage(new Uri(Path.GetFullPath(args[2]))) : null;
        var lyrics = args.Length > 3 && File.Exists(args[3]) ? File.ReadAllText(args[3]) : Fixture;
        var startsAt = args.FirstOrDefault(arg => arg.StartsWith("--start=", StringComparison.Ordinal));
        var position = startsAt is not null && double.TryParse(startsAt[8..], System.Globalization.CultureInfo.InvariantCulture, out var start)
            ? TimeSpan.FromSeconds(start)
            : TimeSpan.FromSeconds(0.65);
        var duration = Math.Max(16, LrcParser.Parse(lyrics).Last().Time.TotalSeconds + 3);
        using var monitor = new TestMonitor(cover, TimeSpan.FromSeconds(duration));
        var lyricsSource = new FixtureLyrics(lyrics);
        using var sync = new LyricsSyncService(monitor, lyricsSource, settings);
        using var http = new HttpClient();
        var session = new EmptySpotifySession();
        using var likes = new SpotifyLikeService(monitor, sync, settings, session, new SpotifyLyricsProvider(http, session),
            new SpotifyLibraryClient(http), Dispatcher.CurrentDispatcher);
        using var miniVm = new MiniPlayerViewModel(monitor, sync, settings, new AppVolumeService(), likes);
        using var barVm = new TaskbarViewModel(monitor, sync, settings, new AppVolumeService(), likes);
        var mini = new MiniPlayerWindow(miniVm, () => settings.Current.EnableAnimations, () => true);
        var bar = new TaskbarOverlay(barVm) { Left = 22, Top = 485, Width = 560 };
        var softwareOnly = args.Contains("--software");
        var backdrop = new Window
        {
            Left = 0, Top = 0, Width = 704, Height = 560, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(19, 22, 28)),
        };
        var heading = new TextBlock { Margin = new Thickness(24, 22, 20, 0), Foreground = Brushes.White, FontSize = 19 };
        backdrop.Content = heading;
        if (command == "demo")
            backdrop.Show();

        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var inkDriver = typeof(MiniPlayerWindow).GetField("_lyricInk", fields)!.GetValue(mini)!;
        var miniTimer = (DispatcherTimer)inkDriver.GetType().GetField("_timer", fields)!.GetValue(inkDriver)!;
        if (softwareOnly)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            mini.SourceInitialized += (_, _) =>
                ((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(mini)).CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            bar.SourceInitialized += (_, _) =>
                ((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(bar)).CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        async Task Configure(LyricFlowMode flow, LyricMotionMode motion, bool cinema, bool barFlow, int hertz,
            MiniPlayerBackground background = MiniPlayerBackground.Aurora, LyricsAlignment alignment = LyricsAlignment.Left,
            LyricsTypeface typeface = LyricsTypeface.Modern)
        {
            monitor.Pause();
            await monitor.SeekAsync(position);
            settings.Update(s =>
            {
                s.EnableAnimations = true;
                s.PrimaryText = PrimaryText.Lyrics;
                s.PausedBehavior = PausedBehavior.Hide;
                s.Bar.ShowText = true;
                s.Bar.ShowCaption = false;
                s.Bar.ShowCover = false;
                s.Bar.ShowLike = false;
                s.Bar.ShowPrevious = false;
                s.Bar.ShowPlayPause = true;
                s.Bar.ShowNext = false;
                s.Bar.ShowRepeat = false;
                s.TaskbarLyricFlow = barFlow ? LyricFlowMode.Adaptive : LyricFlowMode.Off;
                s.MiniPlayerLook = new MiniPlayerStyle
                {
                    Layout = cinema ? MiniPlayerLayout.Cinema : MiniPlayerLayout.Vertical,
                    Background = background, Alignment = alignment, Typeface = typeface,
                    Controls = ControlsLook.Solid, Highlight = LyricsHighlight.Expressive, Flow = flow, Motion = motion,
                };
            });
            miniTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / hertz);
            mini.ShowAt(new PixelRect(310, 51, 80, 0), new PixelRect(0, 0, 700, 550), 1, preview: true);
            // The normal width anchor is retained, but the comparison keeps the bar below the popup.
            if (!bar.IsVisible)
                bar.Show();
            barVm.SetActive(true);
            await Task.Delay(190);
        }

        TextBlock ActiveText() => All<TextBlock>(mini).First(text => text.DataContext is LyricLineViewModel { IsCurrent: true });
        double CenterError()
        {
            var row = (FrameworkElement)mini.FindName("ControlsPart");
            var button = (FrameworkElement)mini.FindName("PlayPauseButton");
            return Math.Abs(button.TranslatePoint(new Point(button.ActualWidth / 2, 0), row).X - row.ActualWidth / 2);
        }

        await sync.ForceReloadAsync();
        if (command == "smoke")
        {
            foreach (var background in Enum.GetValues<MiniPlayerBackground>())
                foreach (var cinema in new[] { false, true })
                {
                    var i = (int)background;
                    await Configure(LyricFlowMode.Linear, cinema ? LyricMotionMode.Expressive : LyricMotionMode.Smooth, cinema, true, 30,
                        background, i % 2 == 0 ? LyricsAlignment.Center : LyricsAlignment.Left, (LyricsTypeface)(i % 4));
                    await monitor.SeekAsync(TimeSpan.FromSeconds(3.7));
                    await Task.Delay(65);
                    var text = ActiveText();
                    Check(text.OpacityMask is DrawingBrush, $"No fill in {background}, cinema={cinema}");
                    Check(CenterError() < 1, "The play button moved away from center.");
                    Check(text.ActualHeight > text.FontSize * 2, "Long lyric did not wrap.");
                    SaveVisual((FrameworkElement)mini.FindName("ContentCard"), Path.Combine(output, $"wrap-{background}-{cinema}.png"));
                }

            var viewport = (FrameworkElement)mini.FindName("LyricsViewport");
            var before = viewport.RenderSize;
            await monitor.SeekAsync(TimeSpan.FromSeconds(5.4));
            monitor.Play();
            await Task.Delay(200);
            Check(ActiveText().HasAnimatedProperties || ((ScaleTransform)ActiveText().RenderTransform).HasAnimatedProperties,
                "Natural line transition did not animate a visual.");
            Check(before == viewport.RenderSize, "Motion changed the height or width of the lyric viewport.");
            monitor.Pause();
            await monitor.SeekAsync(TimeSpan.FromSeconds(3.7));
            await Task.Delay(70);
            var prior = ActiveText();
            var mask = prior.OpacityMask;
            var f = miniVm.CurrentFill;
            await Task.Delay(110);
            Check(f == miniVm.CurrentFill && ReferenceEquals(mask, prior.OpacityMask), "Paused flow moved or rebuilt its mask.");
            settings.Update(s => s.SyncOffsetMs = 350);
            await Task.Delay(70);
            Check(f != miniVm.CurrentFill, "Offset did not rebase flow.");
            await monitor.SeekAsync(TimeSpan.FromSeconds(12));
            await Task.Delay(70);
            Check(!ReferenceEquals(prior, ActiveText()), "Seek did not switch the active lyric.");
            await monitor.SeekAsync(TimeSpan.FromSeconds(3.4));
            await Task.Delay(60);
            Check(ReferenceEquals(prior, ActiveText()), "Existing lyric visual was recreated on seek.");
            mini.HidePopup();
            await Task.Delay(55);
            Check(prior.OpacityMask is null, "Hidden popup retained a paint mask.");
            var hiddenDriver = typeof(MiniPlayerWindow).GetField("_lyricInk", fields)!.GetValue(mini)!;
            Check(!((DispatcherTimer)hiddenDriver.GetType().GetField("_timer", fields)!.GetValue(hiddenDriver)!).IsEnabled, "Hidden popup still repainted.");
            mini.ShowAt(new PixelRect(310, 51, 80, 0), new PixelRect(0, 0, 700, 550), 1, preview: true);
            await Task.Delay(100);
            Check(ActiveText().OpacityMask is DrawingBrush, "Reopened popup did not restore the current fill.");
            var unresized = ActiveText().OpacityMask;
            mini.Width += 66;
            await Task.Delay(100);
            Check(ActiveText().OpacityMask is DrawingBrush resized && !ReferenceEquals(unresized, resized), "Resized lyric retained stale mask geometry.");
            var front = (TextBlock)typeof(TaskbarOverlay).GetField("_front", fields)!.GetValue(bar)!;
            Check(front.OpacityMask is DrawingBrush, "Taskbar did not get independent fill.");
            settings.Update(s => { s.TaskbarLyricFlow = LyricFlowMode.Off; s.MiniPlayerLook.Flow = LyricFlowMode.Off; });
            await Task.Delay(80);
            Check(front.OpacityMask is null && ActiveText().OpacityMask is null, "Disabling flow did not reset the visual.");
            settings.Update(s => { s.EnableAnimations = false; s.MiniPlayerLook.Motion = LyricMotionMode.Expressive; });
            await monitor.SeekAsync(TimeSpan.FromSeconds(5.1));
            monitor.Play();
            await Task.Delay(500);
            Check(!ActiveText().HasAnimatedProperties && !((ScaleTransform)ActiveText().RenderTransform).HasAnimatedProperties,
                "App motion preference was ignored.");
            monitor.Pause();
            settings.Update(s => s.EnableAnimations = true);
            await monitor.SeekAsync(TimeSpan.FromSeconds(5.42));
            monitor.Play();
            await Task.Delay(190);
            var orphan = ActiveText();
            monitor.ChangeTrack();
            await Task.Delay(480);
            Check(sync.Timeline is not null && monitor.Track.Title == "Second fixture" && !All<TextBlock>(mini).Contains(orphan),
                "Track change retained a visual from the prior lyric.");
            monitor.Pause();
            settings.Update(s => { s.Bar.ShowCaption = true; s.MarqueeLongText = true; s.TaskbarLyricFlow = LyricFlowMode.Linear; });
            bar.Width = 210;
            await monitor.SeekAsync(TimeSpan.FromSeconds(3.5));
            await Task.Delay(100);
            front = (TextBlock)typeof(TaskbarOverlay).GetField("_front", fields)!.GetValue(bar)!;
            Check(front.OpacityMask is DrawingBrush, "Taskbar marquee lost its fill.");
            bar.Hide();
            Check(front.OpacityMask is null, "Hidden bar retained its lyric paint.");
            File.WriteAllText(Path.Combine(output, "smoke.txt"), "Passed backgrounds × layouts, font/alignment combinations, multiline fill, pause, offset, seek, reuse, visibility, track change, playback controls centering and independent taskbar fill.");
            return;
        }

        var cases = new (string Label, LyricFlowMode Flow, LyricMotionMode Motion, bool Bar, bool Cinema, int Hz)[]
        {
            ("A · Existing transition", LyricFlowMode.Off, LyricMotionMode.Off, false, false, 30),
            ("B · Smooth", LyricFlowMode.Off, LyricMotionMode.Smooth, false, false, 30),
            ("B2 · Expressive", LyricFlowMode.Off, LyricMotionMode.Expressive, false, true, 30),
            ("C · Linear", LyricFlowMode.Linear, LyricMotionMode.Off, false, false, 30),
            ("D · Adaptive", LyricFlowMode.Adaptive, LyricMotionMode.Off, false, false, 30),
            ("E · Adaptive + Expressive", LyricFlowMode.Adaptive, LyricMotionMode.Expressive, false, true, 30),
            ("F · Adaptive + Expressive + taskbar", LyricFlowMode.Adaptive, LyricMotionMode.Expressive, true, true, 30),
            ("G · Automatic / Enhanced LRC", LyricFlowMode.Automatic, LyricMotionMode.Smooth, false, true, 30),
            ("E60 · Adaptive + Expressive", LyricFlowMode.Adaptive, LyricMotionMode.Expressive, false, true, 60),
        };
        if (command == "demo")
        {
            var seconds = args.Length > 4 && int.TryParse(args[4], out var requested) ? requested : 9;
            var scenarios = args.Length > 5 ? args[5].Split(',') : [];
            var firstShot = true;
            foreach (var item in cases.Where(item => item.Hz == 30 && (scenarios.Length == 0 || scenarios.Contains(item.Label.Split(' ')[0]))))
            {
                heading.Text = item.Label + (item.Cinema ? "  /  Cinema Wide" : "  /  Vertical");
                await Configure(item.Flow, item.Motion, item.Cinema, item.Bar, item.Hz);
                if (firstShot)
                {
                    firstShot = false;
                    File.WriteAllText(Path.Combine(output, "demo.ready"), "capture stage is visible");
                    await Task.Delay(900);
                }
                monitor.Play();
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                mini.HidePopup();
            }
            return;
        }
        if (command != "bench")
            throw new ArgumentException("Unknown review command " + command);
        // Warm the GPU, text formatter, and both effect paths outside the timed windows.
        await Configure(LyricFlowMode.Adaptive, LyricMotionMode.Expressive, cinema: true, barFlow: true, 60);
        monitor.Play();
        await Task.Delay(4500);
        mini.HidePopup();
        using var csv = new StreamWriter(Path.Combine(output, "metrics.csv")) { AutoFlush = true };
        csv.WriteLine("scenario,phase,seconds,cpu_ms_per_s,private_mib,working_mib,allocated_kib_per_s,dispatcher_p95_ms,control_dispatch_ms,render_tier,software_only,session_start_local,flow_ticks,flow_p95_ms");
        foreach (var item in cases.Where(item => !item.Label.StartsWith("G ", StringComparison.Ordinal)
            && item.Label.StartsWith(args.Length > 4 && !args[4].StartsWith("--", StringComparison.Ordinal) ? args[4] : "", StringComparison.Ordinal)))
        {
            // Fix the same layout and keep the bar visible in every timed comparison.
            await Configure(item.Flow, item.Motion, cinema: true, item.Bar, item.Hz);
            monitor.Play();
            await Measure(item.Label.Split(' ')[0], "open", 12);
            mini.HidePopup();
            await Measure(item.Label.Split(' ')[0], "closed", 5);
        }
        if (args.Contains("--long"))
        {
            await Configure(LyricFlowMode.Adaptive, LyricMotionMode.Expressive, cinema: true, barFlow: true, 30);
            monitor.Play();
            await Measure("F", "extended", 90, loop: true);
            mini.HidePopup();
            await Measure("F", "after_extended", 10);
        }

        async Task Measure(string label, string phase, int seconds, bool loop = false)
        {
            using var process = Process.GetCurrentProcess();
            var startLocal = DateTime.Now;
            var watch = Stopwatch.StartNew();
            var startCpu = process.TotalProcessorTime;
            var startAllocated = GC.GetTotalAllocatedBytes(false);
            var delays = new List<double>(seconds * 20 + 10);
            var last = 0.0;
            var lastFlow = 0.0;
            var flowTicks = 0;
            var flowIntervals = new List<double>(seconds * 60);
            void FlowTick(object? sender, EventArgs e)
            {
                var elapsed = watch.Elapsed.TotalMilliseconds;
                if (lastFlow > 0 && elapsed - lastFlow < 100)
                    flowIntervals.Add(elapsed - lastFlow);
                lastFlow = elapsed;
                flowTicks++;
            }
            miniTimer.Tick += FlowTick;
            var controlMilliseconds = 0.0;
            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += async (_, _) =>
            {
                var elapsed = watch.Elapsed.TotalMilliseconds;
                delays.Add(elapsed - last);
                last = elapsed;
                if (controlMilliseconds == 0 && elapsed >= 1300 && mini.IsVisible)
                {
                    var sent = Stopwatch.GetTimestamp();
                    miniVm.PlayPauseCommand.Execute(null);
                    controlMilliseconds = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                    miniVm.PlayPauseCommand.Execute(null);
                }
                if (loop && monitor.Position > TimeSpan.FromSeconds(13))
                    await monitor.SeekAsync(TimeSpan.FromSeconds(0.65));
            };
            timer.Start();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            timer.Stop();
            miniTimer.Tick -= FlowTick;
            process.Refresh();
            var elapsedSeconds = watch.Elapsed.TotalSeconds;
            static double P95(List<double> samples)
            {
                samples.Sort();
                return samples.Count == 0 ? 0 : samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
            }
            csv.WriteLine(FormattableString.Invariant($"{label},{phase},{elapsedSeconds:F3},{(process.TotalProcessorTime - startCpu).TotalMilliseconds / elapsedSeconds:F3},{process.PrivateMemorySize64 / 1048576.0:F2},{process.WorkingSet64 / 1048576.0:F2},{(GC.GetTotalAllocatedBytes(false) - startAllocated) / 1024.0 / elapsedSeconds:F2},{P95(delays):F2},{controlMilliseconds:F3},{RenderCapability.Tier >> 16},{softwareOnly},{startLocal:yyyy-MM-dd HH:mm:ss.fff},{flowTicks},{P95(flowIntervals):F2}"));
        }
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in All<T>(child))
                yield return nested;
        }
    }

    private static void SaveVisual(FrameworkElement visual, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Round(visual.ActualWidth), (int)Math.Round(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var target = new DrawingVisual();
        using (var context = target.RenderOpen())
            context.DrawRectangle(new VisualBrush(visual) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(visual.RenderSize) }, null, new Rect(visual.RenderSize));
        bitmap.Render(target);
        File.WriteAllBytes(path, LyricCardRenderer.EncodePng(bitmap));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private const string Fixture = """
        [00:00.40]At the edge of the evening
        [00:02.00]We ran through the city, carrying everything we had across the river
        [00:05.55]Go
        [00:05.85]Run
        [00:06.20]Breathe again
        [00:07.60]<00:07.60>Hold <00:09.20>on, <00:09.55>the sky is opening<00:10.55>
        [00:11.00]Stay
        [00:16.00]
        [00:16.65]All the windows turn to gold
        """;
}

internal sealed class FixtureLyrics(string lrc) : ILyricsService
{
    private readonly LyricsResult _result = new(LyricsKind.Synced, LrcParser.Parse(lrc), null) { Source = "Review fixture" };
    public Task<LyricsResult> GetLyricsAsync(TrackQuery query, CancellationToken cancellationToken) => Task.FromResult(_result);
    public Task ForgetAsync(TrackQuery query) => Task.CompletedTask;
}

internal sealed class EmptySpotifySession : ISpotifyLyricsSession
{
    public SpotifySearchTemplate? SearchTemplate => null;
    public SpotifyCredentials? ValidCredentials => null;
    public Task<SpotifyCredentials?> GetCredentialsAsync(TrackQuery? query, CancellationToken cancellationToken) => Task.FromResult<SpotifyCredentials?>(null);
    public Task<SpotifyCredentials?> RenewCredentialsAsync(SpotifyCredentials previous, CancellationToken cancellationToken) => Task.FromResult<SpotifyCredentials?>(null);
    public Task<IReadOnlyList<SpotifyTrack>> SearchAsync(TrackQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SpotifyTrack>>([]);
    public void ReportStatus(string key) { }
}

internal sealed class TestMonitor(ImageSource? artwork, TimeSpan duration) : ISpotifyMonitor
{
    private readonly PlaybackClock _clock = new() { Duration = duration };
    public bool HasSession => true;
    public string? SessionAppId => "review";
    public IReadOnlyList<string> AllSessionAppIds => ["review"];
    public TrackInfo Track { get; private set; } = new("Lyric Flow", "Taskitify", "Motion Review");
    public PlaybackState Status => _clock.IsPlaying ? PlaybackState.Playing : PlaybackState.Paused;
    public TimeSpan Position => _clock.Position;
    public TimeSpan? Duration => _clock.Duration;
    public ImageSource? Thumbnail => artwork;
    public bool CanSeek => true;
    public bool CanGoNext => true;
    public bool CanGoPrevious => true;
    public bool CanShuffle => false;
    public bool CanRepeat => false;
    public bool IsShuffleActive => false;
    public RepeatMode RepeatMode => RepeatMode.None;
    public (Color Start, Color End) Palette => (Color.FromRgb(34, 50, 61), Color.FromRgb(56, 105, 120));
    public event EventHandler? TrackChanged;
    public event EventHandler? PlaybackChanged;
    public event EventHandler? TimelineChanged;
    public event EventHandler? ThumbnailChanged { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
    public Task PlayPauseAsync()
    {
        if (_clock.IsPlaying)
            Pause();
        else
            Play();
        return Task.CompletedTask;
    }
    public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position)
    {
        _clock.Reset(position);
        TimelineChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
    public void Play()
    {
        _clock.SetPlaying(true);
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Pause()
    {
        _clock.SetPlaying(false);
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangeTrack()
    {
        Track = new TrackInfo("Second fixture", Track.Artist, Track.Album);
        _clock.Reset();
        TrackChanged?.Invoke(this, EventArgs.Empty);
    }
    public Task ToggleShuffleAsync() => Task.CompletedTask;
    public Task CycleRepeatAsync() => Task.CompletedTask;
    public void Dispose() { }
}
