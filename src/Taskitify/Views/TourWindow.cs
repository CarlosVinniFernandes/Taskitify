using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.Views;

/// <summary>Where users can write to the developer. One place to change it.</summary>
internal static class ContactInfo
{
    // TODO before release: confirm the full address on carlosfernandes.dev (none was found in the project).
    public const string Email = "hello@carlosfernandes.dev";
    public const string Repository = OfficialLinks.Repository;

    public static string MailTo => $"mailto:{Email}?subject={Uri.EscapeDataString("Taskitify — feedback")}";
}

/// <summary>
/// The welcome tour: nine steps in the campaign's look, with the real taskbar player and mini player
/// playing demonstration songs (see <see cref="TourStage"/>). Nothing it shows changes the user's
/// settings, playback, lyrics cache or Spotify session.
/// </summary>
public sealed class TourWindow : Window
{
    /// <summary>Raise when the tour changes enough to show it again to users who saw an older one.</summary>
    public const int Version = 1;

    private const double DesignWidth = 1120, DesignHeight = 720;
    private static readonly Color Cream = Color.FromRgb(0xF1, 0xF2, 0xED);
    private static readonly Brush CreamBrush = Frozen(Cream);
    private static readonly Brush Muted = Frozen(Color.FromArgb(0xE2, 0xF1, 0xF2, 0xED));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0xA8, 0xF1, 0xF2, 0xED));
    private static readonly Brush Turquoise = Frozen(Color.FromRgb(0x5C, 0xD2, 0xE0));
    private static readonly FontFamily Display = LyricCardRenderer.InterDisplay;
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly Func<SettingsPage, bool> _openSettings;
    private readonly bool _spotifyConnected;
    private readonly TourStage _stage;
    private readonly SettingsService _real;
    private readonly List<Step> _steps = [];
    private readonly Grid _content = new();
    private readonly Border _backA = new(), _backB = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back, _next, _skip, _playlist;
    private bool _useA = true;
    private int _index = -1;

    private sealed record Step(string Key, Func<FrameworkElement> Build, Brush Background, bool CustomActions = false, bool Bar = false, bool Mini = false)
    {
        public FrameworkElement? View;
        public List<DispatcherTimer> Timers { get; } = [];
    }

    public TourWindow(Func<SettingsPage, bool> openSettings, SettingsService settings)
    {
        _openSettings = openSettings;
        _real = settings;
        _spotifyConnected = settings.Current.SpotifyLyricsEnabled;
        // The demonstrations start from the user's own choices.
        _stage = new TourStage(settings.Current);
        Title = T("WindowTitle");
        Background = Frozen(Color.FromRgb(0x0D, 0x1E, 0x29));
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        Foreground = CreamBrush;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1180, work.Width * .92);
        Height = Math.Min(Width * DesignHeight / DesignWidth + 40, work.Height * .92);
        MinWidth = 720;
        MinHeight = 480;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        _back = Ghost(T("Back"), () => Go(_index - 1));
        _next = Primary(T("Continue"), () => Go(_index + 1));
        _skip = Link(T("Skip"), Close);
        _playlist = Primary("\uE7F6  " + T("CommunityListen"), () => OfficialLinks.Open(OfficialLinks.Playlist));
        ((TextBlock)_playlist.Content).Inlines.Add(new System.Windows.Documents.Run("   \uE8A7") { FontFamily = Icons, FontSize = 12 });
        _playlist.Margin = new Thickness(12, 0, 0, 0);
        _skip.ToolTip = T("SkipHint");

        _steps.Add(new("Welcome", BuildWelcome, Atmosphere("#0D2230", "#173F4F", "#8C8A66"), CustomActions: true, Bar: true, Mini: true));
        _steps.Add(new("Sources", BuildSources, Atmosphere("#0F1E2E", "#1C3A4A", "#2F5E5A")));
        _steps.Add(new("Taskbar", BuildTaskbar, Atmosphere("#0B1C27", "#16384A", "#356B79"), Bar: true, Mini: true));
        _steps.Add(new("Adapt", BuildAdapt, Atmosphere("#101B2A", "#22364F", "#6E5A72"), Bar: true));
        _steps.Add(new("Mini", BuildMini, Atmosphere("#0E1A26", "#1E3346", "#5A4A6A"), Mini: true));
        _steps.Add(new("Minimal", BuildMinimal, Atmosphere("#0A1420", "#14283A", "#3A4C66")));
        _steps.Add(new("Studio", BuildStudio, Atmosphere("#1A1622", "#3A2E3A", "#9A7B52")));
        _steps.Add(new("Privacy", BuildPrivacy, Atmosphere("#0C1A20", "#163037", "#2E5A55")));
        _steps.Add(new("Community", BuildCommunity, Atmosphere("#12182A", "#23315A", "#8A5A6E")));
        _steps.Add(new("Home", BuildHome, Atmosphere("#111C2A", "#2A3550", "#B08A55"), CustomActions: true));

        // The atmosphere fills the whole window; only the content keeps the design proportions.
        _backB.Opacity = 0;
        Content = new Grid { Children = { _backA, _backB, new Viewbox { Stretch = Stretch.Uniform, Child = BuildShell() } } };
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => Go(0);
        Closed += (_, _) =>
        {
            foreach (var step in _steps)
                StopTimers(step);
            _stage.Dispose();
        };
    }

    private static string T(string key) => LocalizationService.Get("Tour" + key);

    // ---- Shell: background, top bar, content, bottom bar ------------------------------------------

    private Grid BuildShell()
    {
        var root = new Grid { Width = DesignWidth, Height = DesignHeight, ClipToBounds = true };

        _content.Margin = new Thickness(56, 76, 56, 92);
        root.Children.Add(_content);

        var top = new Grid { Height = 64, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 8, 32, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Taskitify;component/Resources/Taskitify.ico")), Width = 22, Height = 22 });
        brand.Children.Add(new TextBlock { Text = "Taskitify", FontFamily = Display, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(brand);
        top.Children.Add(_dots);
        _skip.HorizontalAlignment = HorizontalAlignment.Right;
        top.Children.Add(_skip);
        root.Children.Add(top);

        var bottom = new Grid { Height = 72, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 12) };
        var contact = Link("  " + T("Contact"), () => Open(ContactInfo.MailTo));
        contact.ToolTip = T("ContactHint");
        AutomationProperties(contact, T("ContactHint"));
        contact.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(contact);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _back.Margin = new Thickness(0, 0, 12, 0);
        actions.Children.Add(_back);
        actions.Children.Add(_next);
        actions.Children.Add(_playlist);
        bottom.Children.Add(actions);
        root.Children.Add(bottom);

        for (var i = 0; i < _steps.Count; i++)
        {
            var target = i;
            var dot = new Button
            {
                Width = 22, Height = 22, Cursor = Cursors.Hand, Focusable = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Template = DotTemplate(), ToolTip = $"{i + 1} / {_steps.Count}",
            };
            System.Windows.Automation.AutomationProperties.SetName(dot, $"{T("Step")} {i + 1}");
            dot.Click += (_, _) => Go(target);
            _dots.Children.Add(dot);
        }
        return root;
    }

    private static ControlTemplate DotTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var pill = new FrameworkElementFactory(typeof(Border), "Pill");
        pill.SetValue(Border.HeightProperty, 6.0);
        pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        pill.SetBinding(Border.WidthProperty, new System.Windows.Data.Binding("Tag") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        pill.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        grid.AppendChild(pill);
        template.VisualTree = grid;
        return template;
    }

    private void Go(int index)
    {
        if (index >= _steps.Count)
        {
            Close();
            return;
        }
        if (index < 0 || index == _index)
            return;
        var forward = index > _index;
        if (_index >= 0)
            StopTimers(_steps[_index]);
        var old = _index >= 0 ? _steps[_index].View : null;
        _index = index;
        var step = _steps[index];
        step.View ??= step.Build();
        _stage.Use(step.Bar, step.Mini);
        _stage.SetPlaying(step.Key != "Adapt");
        foreach (var timer in step.Timers)
            timer.Start();

        // Background crossfade.
        var (show, hide) = _useA ? (_backB, _backA) : (_backA, _backB);
        _useA = !_useA;
        show.Background = step.Background;
        Fade(show, 1, 260);
        Fade(hide, 0, 260);

        if (old is not null)
            _content.Children.Remove(old);
        if (!_content.Children.Contains(step.View))
            _content.Children.Add(step.View);
        step.View.Opacity = 0;
        var slide = new TranslateTransform(forward ? 18 : -18, 0);
        step.View.RenderTransform = slide;
        Fade(step.View, 1, 220);
        Animate(slide, TranslateTransform.XProperty, 0, 240);

        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var dot = (Button)_dots.Children[i];
            dot.Tag = i == index ? 22.0 : 6.0;
            dot.Foreground = i == index ? CreamBrush : Faint;
            dot.Width = i == index ? 30 : 14;
        }
        _back.Visibility = index > 0 ? Visibility.Visible : Visibility.Hidden;
        _next.Visibility = step.CustomActions ? Visibility.Collapsed : Visibility.Visible;
        var community = step.Key == "Community";
        _playlist.Visibility = community ? Visibility.Visible : Visibility.Collapsed;
        _next.Background = community ? Surface : CreamBrush;
        _next.Foreground = community ? CreamBrush : Frozen(Color.FromRgb(0x10, 0x20, 0x2A));
        _next.BorderBrush = community ? SurfaceLine : Brushes.Transparent;
        _next.BorderThickness = new Thickness(community ? 1 : 0);
        _next.Effect = community ? null : PrimaryShadow;
        _skip.Visibility = index < _steps.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        if (!step.CustomActions)
            _next.Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Right or Key.PageDown: Go(_index + 1); e.Handled = true; break;
            case Key.Left or Key.PageUp: Go(_index - 1); e.Handled = true; break;
        }
    }

    private static void StopTimers(Step step)
    {
        foreach (var timer in step.Timers)
            timer.Stop();
    }

    private void Every(Step step, double ms, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => tick();
        step.Timers.Add(timer);
        if (_index >= 0 && _steps[_index] == step)
            timer.Start();
    }

    private Step Current(string key) => _steps.First(s => s.Key == key);

    // ---- 01 Welcome ----------------------------------------------------------------------------

    private FrameworkElement BuildWelcome()
    {
        var grid = Columns(430);
        var text = Stack(
            Eyebrow(T("WelcomeEyebrow")),
            Headline(T("WelcomeTitle"), 48),
            Body(T("WelcomeBody"), 17, top: 18),
            Body(T("WelcomeThanks"), 17, top: 12, brush: CreamBrush),
            Body(T("WelcomePitch"), 15, top: 18));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        var tour = Primary(T("WelcomeTour"), () => Go(1));
        buttons.Children.Add(tour);
        text.Children.Add(buttons);
        Loaded += (_, _) => tour.Focus();
        grid.Children.Add(text);

        var visual = new Grid { Margin = new Thickness(36, 0, 0, 0) };
        var mini = Live(Current("Welcome"), () => _stage.MiniVisual(), 560);
        mini.VerticalAlignment = VerticalAlignment.Top;
        mini.Margin = new Thickness(0, 10, 0, 0);
        visual.Children.Add(mini);
        var bar = TaskbarMock(Current("Welcome"), 560, compact: true);
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        bar.Margin = new Thickness(-20, 0, -20, 6);
        visual.Children.Add(bar);
        Grid.SetColumn(visual, 1);
        grid.Children.Add(visual);
        return grid;
    }

    // ---- 02 Lyric sources ----------------------------------------------------------------------

    private FrameworkElement BuildSources()
    {
        var root = new DockPanel();
        var head = Stack(Eyebrow(T("SourcesEyebrow")), Headline(T("SourcesTitle"), 40), Body(T("SourcesBody"), 16, top: 10));
        head.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (TextBlock t in head.Children) t.TextAlignment = TextAlignment.Center;
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = Stack(Body(T("SourcesReady"), 16, brush: CreamBrush));
        ((TextBlock)foot.Children[0]).TextAlignment = TextAlignment.Center;
        var connect = Ghost("\uE713  " + T("SourcesConnect"), () => _openSettings(SettingsPage.Lyrics));
        connect.Margin = new Thickness(0, 14, 0, 0);
        connect.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Children.Add(connect);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 26, 0, 18) };
        cards.Children.Add(SourceCard("", "#5CD2E0", T("LrclibName"), T("LrclibTag"), T("LrclibBody"), T("LrclibBadge"), !_spotifyConnected, enabledBadge: true));
        cards.Children.Add(SourceCard("", "#1ED760", T("SpotifyName"), T("SpotifyTag"), T("SpotifyBody"), T("SpotifyBadge"), false));
        cards.Children.Add(SourceCard("", "#E2C46F", T("BothName"), T("BothTag"), T("BothBody"), T("BothBadge"), _spotifyConnected));
        root.Children.Add(cards);
        return root;
    }

    private Border SourceCard(string glyph, string color, string name, string tag, string body, string badge, bool active, bool enabledBadge = false)
    {
        var accent = (Color)ColorConverter.ConvertFromString(color);
        var stack = new StackPanel();
        var icon = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Frozen(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
            Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 20, Foreground = Frozen(accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = name, FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 24, Margin = new Thickness(0, 16, 0, 0) });
        stack.Children.Add(new TextBlock { Text = tag, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Frozen(accent), Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(Body(body, 14, top: 10));
        var pill = new Border
        {
            Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 4),
            Background = Frozen(Color.FromArgb(active || enabledBadge ? (byte)0x30 : (byte)0x18, accent.R, accent.G, accent.B)),
            Child = new TextBlock { Text = (enabledBadge ? "✓  " : "") + badge, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = active || enabledBadge ? Frozen(accent) : Muted },
        };
        stack.Children.Add(pill);
        if (active)
            stack.Children.Add(new TextBlock { Text = T("SourcesActive"), FontSize = 12, Foreground = Faint, Margin = new Thickness(2, 8, 0, 0) });
        return new Border
        {
            Margin = new Thickness(9), Padding = new Thickness(24, 24, 22, 22), CornerRadius = new CornerRadius(18),
            Background = active ? Frozen(Color.FromRgb(0x1A, 0x2B, 0x36)) : Card,
            BorderBrush = active ? Frozen(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)) : CardLine,
            BorderThickness = new Thickness(active ? 1.5 : 1), Child = stack,
        };
    }

    // ---- 03–05 Guided setup: taskbar, paused, mini player ------------------------------------------
    // The stage starts from the user's own settings. Choices change the demonstration at once; only
    // "Save" writes them, and only what differs, to the real settings. "Undo" goes back to them.

    private static readonly Brush CalloutBack = Frozen(Color.FromRgb(0x12, 0x25, 0x2E));
    private static readonly Brush CalloutLine = Frozen(Color.FromArgb(0x99, 0x5C, 0xD2, 0xE0));
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x8C, 0x05, 0x0B, 0x11));
    private Grid? _lastSlot;
    private Image? _lastPlayer;

    private static BarLayout CopyBar(BarLayout b) => new()
    {
        ShowCover = b.ShowCover, ShowText = b.ShowText, ShowCaption = b.ShowCaption, ShowShuffle = b.ShowShuffle, ShowPrevious = b.ShowPrevious,
        ShowPlayPause = b.ShowPlayPause, ShowNext = b.ShowNext, ShowRepeat = b.ShowRepeat, ShowVolume = b.ShowVolume, ShowLike = b.ShowLike,
        ShowProgress = b.ShowProgress, Width = b.Width,
    };

    private static bool SameBar(BarLayout a, BarLayout b) =>
        a.ShowCover == b.ShowCover && a.ShowText == b.ShowText && a.ShowCaption == b.ShowCaption && a.ShowPrevious == b.ShowPrevious
        && a.ShowPlayPause == b.ShowPlayPause && a.ShowNext == b.ShowNext && a.ShowLike == b.ShowLike && a.ShowProgress == b.ShowProgress;

    /// <summary>The dimmed stage with a lit window around one element, a ring and a callout with an arrow.</summary>
    private sealed class Spotlight
    {
        private readonly Grid _host;
        private readonly Path _dim = new() { Fill = Dim, IsHitTestVisible = false };
        private readonly Border _ring = new() { BorderBrush = Turquoise, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), IsHitTestVisible = false };
        private readonly Polygon _arrow = new() { Fill = CalloutBack, Stroke = CalloutLine, StrokeThickness = 1 };
        private readonly Canvas _layer = new();
        private readonly bool _above;
        private Rect _last = Rect.Empty;

        public Spotlight(Grid host, FrameworkElement callout, bool above)
        {
            _host = host;
            _above = above;
            Callout = callout;
            _layer.Children.Add(_dim);
            _layer.Children.Add(_ring);
            _layer.Children.Add(Callout);
            _layer.Children.Add(_arrow);
            Grid.SetRow(_layer, 1);
            Grid.SetColumnSpan(_layer, 4);
            host.Children.Add(_layer);
        }

        public FrameworkElement Callout { get; }

        /// <summary>The space the spotlight covers: target rectangles are given relative to it.</summary>
        public Visual Layer => _layer;

        public Func<Rect?> Target { get; set; } = () => null;

        public void Update()
        {
            if (!_layer.IsLoaded || _layer.ActualWidth <= 0 || Target() is not { } target)
                return;
            target.Inflate(6, 6);
            if (target == _last && Callout.ActualHeight > 0)
                return;
            _last = target;
            var all = new Rect(0, 0, _layer.ActualWidth, _layer.ActualHeight);
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(target, 12, 12));
            Canvas.SetLeft(_ring, target.X);
            Canvas.SetTop(_ring, target.Y);
            _ring.Width = target.Width;
            _ring.Height = target.Height;
            Callout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (w, h) = (Callout.DesiredSize.Width, Callout.DesiredSize.Height);
            if (_above)
            {
                var left = Math.Clamp(target.X + target.Width / 2 - w / 2, 8, Math.Max(8, all.Width - w - 8));
                var top = Math.Max(8, target.Y - 16 - h);
                Canvas.SetLeft(Callout, left);
                Canvas.SetTop(Callout, top);
                var x = Math.Clamp(target.X + target.Width / 2, left + 24, left + w - 24);
                var y = top + h - 1;
                _arrow.Points = [new Point(x - 11, y), new Point(x + 11, y), new Point(x, y + 12)];
            }
            else
            {
                var left = Math.Min(all.Width - w - 8, target.Right + 22);
                var top = Math.Clamp(target.Y + target.Height / 2 - h / 2, 8, Math.Max(8, all.Height - h - 8));
                Canvas.SetLeft(Callout, left);
                Canvas.SetTop(Callout, top);
                var y = Math.Clamp(target.Y + target.Height / 2, top + 24, top + h - 24);
                var x = left + 1;
                _arrow.Points = [new Point(x, y - 11), new Point(x, y + 11), new Point(x - 12, y)];
            }
        }
    }

    private static Rect? Bounds(FrameworkElement? element, Visual host)
    {
        if (element is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 })
            return null;
        try { return element.TransformToVisual(host).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>A compact panel in the style of the reference: eyebrow, title, a line of text, controls, actions.</summary>
    private static Border Callout(string eyebrow, string title, string body, UIElement content, UIElement actions, double width = 420)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = eyebrow.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Turquoise });
        stack.Children.Add(new TextBlock { Text = title, FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 24, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        var text = Body(body, 13.5, top: 6);
        stack.Children.Add(text);
        stack.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = content });
        stack.Children.Add(new Border { Margin = new Thickness(0, 16, 0, 0), Child = actions });
        return new Border
        {
            Width = width, Padding = new Thickness(22, 18, 22, 18), CornerRadius = new CornerRadius(14),
            Background = CalloutBack, BorderBrush = CalloutLine, BorderThickness = new Thickness(1), Child = stack,
        };
    }

    /// <summary>A check box row in the tour's style; returns a refresh for when the value changes elsewhere (undo).</summary>
    private static (Button Row, Action Refresh) Check(string text, Func<bool> isOn, Action<bool> set)
    {
        var box = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 10, 0) };
        var mark = new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Frozen(Color.FromRgb(0x10, 0x20, 0x2A)) };
        box.Child = mark;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        void Refresh()
        {
            var on = isOn();
            box.Background = on ? Turquoise : Brushes.Transparent;
            box.BorderBrush = on ? Turquoise : SurfaceLineStatic;
            mark.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }
        var button = Make("", () => { }, Brushes.Transparent, CreamBrush, null, 14, new Thickness(6, 6, 10, 6), FontWeights.Normal, radius: 8);
        button.Content = row;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => { set(!isOn()); Refresh(); };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        Refresh();
        return (button, Refresh);
    }

    private static readonly Brush SurfaceLineStatic = Frozen(Color.FromRgb(0x6A, 0x80, 0x8C));

    private static StackPanel Actions(Button primary, Button? secondary, Button? undo)
    {
        var row = new DockPanel { LastChildFill = false };
        if (undo is not null)
        {
            DockPanel.SetDock(undo, Dock.Left);
            row.Children.Add(undo);
        }
        DockPanel.SetDock(primary, Dock.Right);
        row.Children.Add(primary);
        if (secondary is not null)
        {
            secondary.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(secondary, Dock.Right);
            row.Children.Add(secondary);
        }
        return new StackPanel { Children = { row } };
    }

    private static Button Small(Button button)
    {
        button.Padding = new Thickness(16, 8, 16, 9);
        button.FontSize = 13.5;
        return button;
    }

    private Grid SetupScene(Step step, out Border bar, Action? onPlayerClick = null)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var desktop = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Background = PhotoBrush("fuji.jpg", .45, Stretch.UniformToFill), Margin = new Thickness(0, 14, 0, 0) };
        Grid.SetRow(desktop, 1);
        root.Children.Add(desktop);
        // Only the taskbar's right side (the player, the tray, the clock), large enough to read.
        bar = TaskbarMock(step, 1008 / 1.7, compact: false, onPlayerClick, bare: true);
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        bar.LayoutTransform = new ScaleTransform(1.7, 1.7);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        return root;
    }

    private FrameworkElement BuildTaskbar()
    {
        var step = Current("Taskbar");
        FrameworkElement? opened = null;
        var root = SetupScene(step, out _, onPlayerClick: () => { if (opened is not null) Fade(opened, opened.Opacity < .5 ? 1 : 0, 200); });
        root.Children.Add(Stack(Eyebrow(T("TaskbarEyebrow")), Headline(T("TaskbarTitle"), 30)));
        var slot = _lastSlot!;
        var player = _lastPlayer!;

        var popup = Live(step, () => _stage.MiniVisual(), 360);
        popup.HorizontalAlignment = HorizontalAlignment.Right;
        popup.VerticalAlignment = VerticalAlignment.Bottom;
        popup.Margin = new Thickness(0, 0, 200, 104);
        popup.Opacity = 0;
        popup.IsHitTestVisible = false;
        Grid.SetRow(popup, 1);
        opened = popup;

        var checks = new List<Action>();
        var grid = new UniformGrid { Columns = 2 };
        void Add(string label, Func<BarLayout, bool> get, Action<BarLayout, bool> set)
        {
            var (row, refresh) = Check(label, () => get(_stage.Settings.Current.Bar), on => _stage.Update(s => set(s.Bar, on)));
            checks.Add(refresh);
            grid.Children.Add(row);
        }
        Add(T("ElementCover"), b => b.ShowCover, (b, v) => b.ShowCover = v);
        Add(T("ElementLyrics"), b => b.ShowText, (b, v) => b.ShowText = v);
        Add(T("ElementSong"), b => b.ShowCaption, (b, v) => b.ShowCaption = v);
        Add(T("ElementControls"), b => b.ShowPlayPause, (b, v) => { b.ShowPrevious = v; b.ShowPlayPause = v; b.ShowNext = v; });
        Add(T("ElementLike"), b => b.ShowLike, (b, v) => b.ShowLike = v);
        Add(T("ElementProgress"), b => b.ShowProgress, (b, v) => b.ShowProgress = v);

        var content = new StackPanel();
        content.Children.Add(grid);
        var hint = IconText("  " + T("TaskbarClick"));
        hint.FontSize = 12.5;
        hint.Foreground = Muted;
        hint.Margin = new Thickness(6, 10, 0, 0);
        content.Children.Add(hint);

        var save = Small(Primary(T("SetupSave"), () =>
        {
            if (!SameBar(_stage.Settings.Current.Bar, _real.Current.Bar))
                _real.Update(s => s.Bar = CopyBar(_stage.Settings.Current.Bar) is var b ? Keep(b, s.Bar.Width) : s.Bar);
            Go(_index + 1);
        }));
        var undo = Link(T("SetupUndo"), () => { _stage.Update(s => s.Bar = CopyBar(_real.Current.Bar)); checks.ForEach(c => c()); });
        var skip = Link(T("SetupSkip"), () => Go(_steps.FindIndex(s => s.Key == "Minimal")));
        var callout = Callout(T("SetupStep1"), T("SetupBarTitle"), T("SetupBarBody"), content, Actions(save, null, undo));
        ((StackPanel)callout.Child).Children.Add(skip);
        skip.HorizontalAlignment = HorizontalAlignment.Left;
        skip.Margin = new Thickness(-10, 6, 0, -6);
        var spot = new Spotlight(root, callout, above: true);
        spot.Target = () => Bounds(player.IsVisible ? player : slot, spot.Layer);
        Every(step, 150, spot.Update);
        root.Children.Add(popup);
        Panel.SetZIndex(popup, 50);
        return root;
    }

    private static BarLayout Keep(BarLayout bar, int width)
    {
        bar.Width = width;
        return bar;
    }

    private FrameworkElement BuildAdapt()
    {
        var step = Current("Adapt");
        var root = SetupScene(step, out _);
        root.Children.Add(Stack(Eyebrow(T("AdaptEyebrow")), Headline(T("SetupPausedHeading"), 30)));
        var slot = _lastSlot!;

        var explain = Body("", 13.5);
        Button keep = null!, give = null!, play = null!;
        void Show()
        {
            var compact = _stage.Settings.Current.PausedBehavior == PausedBehavior.Minimize;
            Select(keep, compact);
            Select(give, !compact);
            explain.Text = T(compact ? "SetupPausedKeepText" : "SetupPausedGiveText");
            ((TextBlock)play.Content).Inlines.Clear();
            ((TextBlock)play.Content).Inlines.AddRange(IconText(_stage.Monitor.IsPlaying ? "  " + T("SetupPause") : "  " + T("SetupPlay")).Inlines.ToList());
        }
        void Choose(PausedBehavior behavior)
        {
            _stage.Update(s => s.PausedBehavior = behavior);
            _stage.SetPlaying(false);
            Show();
        }
        keep = Segment(T("PausedKeep"), () => Choose(PausedBehavior.Minimize));
        give = Segment(T("PausedGive"), () => Choose(PausedBehavior.Hide));
        play = Segment("", () => { _stage.SetPlaying(!_stage.Monitor.IsPlaying); Show(); });
        var options = new WrapPanel();
        foreach (var b in new[] { keep, give }) { b.Margin = new Thickness(0, 0, 8, 8); options.Children.Add(b); }
        var content = Stack(options, explain);
        play.Margin = new Thickness(0, 12, 0, 0);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(play);

        var save = Small(Primary(T("SetupSave"), () =>
        {
            if (_stage.Settings.Current.PausedBehavior != _real.Current.PausedBehavior)
                _real.Update(s => s.PausedBehavior = _stage.Settings.Current.PausedBehavior);
            Go(_index + 1);
        }));
        var undo = Link(T("SetupUndo"), () => { _stage.Update(s => s.PausedBehavior = _real.Current.PausedBehavior); Show(); });
        var callout = Callout(T("SetupStep2"), T("SetupPausedTitle"), T("SetupPausedBody"), content, Actions(save, null, undo));
        var spot = new Spotlight(root, callout, above: true);
        spot.Target = () => Bounds(slot, spot.Layer);
        Every(step, 150, spot.Update);
        Loaded += (_, _) => Show();
        root.Loaded += (_, _) => Show();
        return root;
    }

    private FrameworkElement BuildMini()
    {
        var step = Current("Mini");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(Stack(Eyebrow(T("MiniEyebrow")), Headline(T("MiniTitle"), 30)));
        var stageArea = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        stageArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        stageArea.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(stageArea, 1);
        root.Children.Add(stageArea);
        var mini = Live(step, () => _stage.MiniVisual(), double.NaN);
        mini.MaxWidth = 540;
        mini.MaxHeight = 440;
        mini.VerticalAlignment = VerticalAlignment.Center;
        stageArea.Children.Add(mini);

        MiniPlayerStyle Look() => _stage.Settings.Current.MiniPlayerLook;
        void Change(Action<MiniPlayerStyle> change) => _stage.Update(s => change(s.MiniPlayerLook));
        var parts = new (string Title, string Body, (string Text, Func<bool> On, Action Apply)[] Options, bool Lyrics)[]
        {
            (T("SetupMiniLayoutTitle"), T("SetupMiniLayoutBody"), [
                (T("LayoutCinema"), () => Look().Layout == MiniPlayerLayout.Cinema, () => Change(l => l.Layout = MiniPlayerLayout.Cinema)),
                (T("LayoutVertical"), () => Look().Layout == MiniPlayerLayout.Vertical, () => Change(l => l.Layout = MiniPlayerLayout.Vertical))], false),
            (T("SetupMiniBackTitle"), T("SetupMiniBackBody"), [
                (T("BackBlur"), () => Look().Background == MiniPlayerBackground.BlurredCover, () => Change(l => l.Background = MiniPlayerBackground.BlurredCover)),
                (T("BackAurora"), () => Look().Background == MiniPlayerBackground.Aurora, () => Change(l => l.Background = MiniPlayerBackground.Aurora)),
                (T("BackVelvet"), () => Look().Background == MiniPlayerBackground.Velvet, () => Change(l => l.Background = MiniPlayerBackground.Velvet)),
                (T("BackAtmosphere"), () => Look().Background == MiniPlayerBackground.Atmosphere, () => Change(l => l.Background = MiniPlayerBackground.Atmosphere))], false),
            (T("SetupMiniTypeTitle"), T("SetupMiniTypeBody"), [
                ("Inter Display", () => Look().Typeface == LyricsTypeface.Modern, () => Change(l => l.Typeface = LyricsTypeface.Modern)),
                ("Serif", () => Look().Typeface == LyricsTypeface.Serif, () => Change(l => l.Typeface = LyricsTypeface.Serif)),
                ("Figtree", () => Look().Typeface == LyricsTypeface.Geometric, () => Change(l => l.Typeface = LyricsTypeface.Geometric)),
                ("Segoe UI", () => Look().Typeface == LyricsTypeface.System, () => Change(l => l.Typeface = LyricsTypeface.System))], true),
            (T("SetupMiniHighTitle"), T("SetupMiniHighBody"), [
                (T("HighExpressive"), () => Look().Highlight == LyricsHighlight.Expressive, () => Change(l => l.Highlight = LyricsHighlight.Expressive)),
                (T("HighClassic"), () => Look().Highlight == LyricsHighlight.Classic, () => Change(l => l.Highlight = LyricsHighlight.Classic)),
                (T("HighProgressive"), () => Look().Highlight == LyricsHighlight.Progressive, () => Change(l => l.Highlight = LyricsHighlight.Progressive))], true),
        };

        var part = 0;
        var eyebrow = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Turquoise };
        var title = new TextBlock { FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 24, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var body = Body("", 13.5, top: 6);
        var options = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        Button prev = null!, next = null!;
        void Render()
        {
            var p = parts[part];
            eyebrow.Text = $"{T("SetupStep3")} · {part + 1}/{parts.Length}".ToUpperInvariant();
            title.Text = p.Title;
            body.Text = p.Body;
            options.Children.Clear();
            var buttons = new List<Button>();
            foreach (var o in p.Options)
            {
                var b = Segment(o.Text, () => { });
                b.Margin = new Thickness(0, 0, 8, 8);
                b.Click += (_, _) => { o.Apply(); for (var i = 0; i < buttons.Count; i++) Select(buttons[i], p.Options[i].On()); };
                Select(b, o.On());
                buttons.Add(b);
                options.Children.Add(b);
            }
            prev.Visibility = part > 0 ? Visibility.Visible : Visibility.Collapsed;
            var label = part < parts.Length - 1 ? T("SetupNext") : T("SetupSave");
            ((TextBlock)next.Content).Text = label;
            System.Windows.Automation.AutomationProperties.SetName(next, label);
        }
        prev = Small(Ghost(T("SetupPrev"), () => { part--; Render(); }));
        next = Small(Primary(T("SetupNext"), () =>
        {
            if (part < parts.Length - 1) { part++; Render(); return; }
            var draft = Look();
            var real = _real.Current.MiniPlayerLook;
            if (draft.Layout != real.Layout || draft.Background != real.Background || draft.Typeface != real.Typeface || draft.Highlight != real.Highlight)
                _real.Update(s =>
                {
                    s.MiniPlayerLook.Layout = draft.Layout;
                    s.MiniPlayerLook.Background = draft.Background;
                    s.MiniPlayerLook.Typeface = draft.Typeface;
                    s.MiniPlayerLook.Highlight = draft.Highlight;
                });
            Go(_index + 1);
        }));
        var undo = Link(T("SetupUndo"), () =>
        {
            var real = _real.Current.MiniPlayerLook;
            Change(l => { l.Layout = real.Layout; l.Background = real.Background; l.Typeface = real.Typeface; l.Highlight = real.Highlight; });
            Render();
        });
        var panel = new StackPanel();
        panel.Children.Add(eyebrow);
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(options);
        panel.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = Actions(next, prev, undo) });
        var callout = new Border
        {
            Width = 380, Padding = new Thickness(22, 18, 22, 18), CornerRadius = new CornerRadius(14),
            Background = CalloutBack, BorderBrush = CalloutLine, BorderThickness = new Thickness(1), Child = panel,
        };
        Render();

        var spot = new Spotlight(root, callout, above: false);
        spot.Target = () =>
        {
            if (Bounds(mini, spot.Layer) is not { } box || _stage.MiniVisual() is not { ActualWidth: > 0 } visual)
                return null;
            var drawn = box;
            if (!parts[part].Lyrics)
                return drawn;
            return Look().Layout == MiniPlayerLayout.Cinema
                ? new Rect(drawn.X + drawn.Width * .36, drawn.Y + drawn.Height * .03, drawn.Width * .62, drawn.Height * .66)
                : new Rect(drawn.X + drawn.Width * .04, drawn.Y + drawn.Height * .24, drawn.Width * .92, drawn.Height * .48);
        };
        Every(step, 150, spot.Update);
        return root;
    }

    /// <summary>A Windows 11-style taskbar with the real Taskitify player docked before the tray.</summary>
    private Border TaskbarMock(Step step, double width, bool compact, Action? onPlayerClick = null, bool bare = false)
    {
        var bar = new Grid { Height = 52 };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        // Windows' Start button, drawn: the icon font has no logo.
        var startGlyph = new UniformGrid { Rows = 2, Columns = 2, Width = 16, Height = 16, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        for (var q = 0; q < 4; q++)
            startGlyph.Children.Add(new Border { Margin = new Thickness(0.8), CornerRadius = new CornerRadius(1.5), Background = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF)) });
        left.Children.Add(startGlyph);
        foreach (var (glyph, color) in new[] { ("", "#F1F2ED"), ("", "#E8C468"), ("", "#5FC4D8"), ("", "#8FB2E8") }.Take(compact ? 2 : 4))
            left.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 18, Width = 40, TextAlignment = TextAlignment.Center, Foreground = Frozen((Color)ColorConverter.ConvertFromString(color)) });
        if (!bare)
            bar.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        var player = LiveImage(step, () => _stage.Bar(1.75), 48);
        player.Cursor = onPlayerClick is null ? null : Cursors.Hand;
        if (onPlayerClick is not null)
            player.MouseLeftButtonUp += (_, _) => onPlayerClick();
        var weather = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        weather.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 18, Foreground = Frozen(Color.FromRgb(0xE8, 0xC4, 0x68)), VerticalAlignment = VerticalAlignment.Center });
        weather.Children.Add(new TextBlock { Text = "22°", FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var slot = new Grid();
        _lastSlot = slot;
        _lastPlayer = player;
        slot.Children.Add(weather);
        slot.Children.Add(player);
        Every(step, 200, () => weather.Visibility = player.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        right.Children.Add(slot);
        foreach (var glyph in new[] { "", "", "", "" })
            right.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var clock = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        clock.Children.Add(new TextBlock { Text = DateTime.Now.ToString("t"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        clock.Children.Add(new TextBlock { Text = DateTime.Now.ToString("d"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        right.Children.Add(clock);
        bar.Children.Add(right);
        return new Border
        {
            Width = width, Background = Frozen(Color.FromArgb(0xEE, 0x14, 0x20, 0x2A)), CornerRadius = new CornerRadius(0, 0, 16, 16),
            BorderBrush = Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0, 1, 0, 0), Child = bar,
        };
    }

    // ---- 06 Minimal Mode (concept) ---------------------------------------------------------------

    private FrameworkElement BuildMinimal()
    {
        var step = Current("Minimal");
        var grid = Columns(360);
        var left = Stack(Eyebrow(T("MinimalEyebrow")), Headline(T("MinimalTitle"), 36), Body(T("MinimalBody"), 15, top: 10));
        foreach (var key in new[] { "MinimalMove", "MinimalAbove", "MinimalAlone", "MinimalOff" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, Foreground = Turquoise, Margin = new Thickness(0, 3, 10, 0), VerticalAlignment = VerticalAlignment.Top });
            row.Children.Add(new TextBlock { Text = T(key), FontSize = 13.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            left.Children.Add(row);
        }
        var badge = new Border
        {
            Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 12, 6),
            Background = Frozen(Color.FromArgb(0x2C, 0xE2, 0xC4, 0x6F)), BorderBrush = Frozen(Color.FromArgb(0x80, 0xE2, 0xC4, 0x6F)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = T("MinimalSoon"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Frozen(Color.FromRgb(0xF0, 0xD8, 0x96)) },
        };
        left.Children.Add(badge);
        grid.Children.Add(left);

        // A desktop with the floating lyrics on it. Only an illustration: it can be dragged and restyled here.
        var screen = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Background = PhotoBrush("fuji.jpg", 0, Stretch.UniformToFill), Margin = new Thickness(36, 0, 0, 0) };
        var canvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
        screen.Child = canvas;
        var lyric = new TextBlock { FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 30, TextAlignment = TextAlignment.Center };
        var caption = new Border
        {
            Padding = new Thickness(26, 12, 26, 14), CornerRadius = new CornerRadius(18), Cursor = Cursors.SizeAll, Child = lyric,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = .55, Color = Colors.Black },
        };
        var song = TourSong.All[0];
        var line = 2;
        var start = DateTime.UtcNow;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var stops = new[] { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 1) };
        foreach (var s in stops) fill.GradientStops.Add(s);
        lyric.Foreground = fill;
        lyric.Text = song.Lines[line];
        Every(step, 33, () =>
        {
            var t = (DateTime.UtcNow - start).TotalSeconds / 3.4;
            if (t >= 1.15)
            {
                start = DateTime.UtcNow;
                line = (line + 1) % song.Lines.Length;
                lyric.Text = song.Lines[line];
                t = 0;
            }
            var p = Math.Min(1, t);
            stops[1].Offset = p;
            stops[2].Offset = Math.Min(1, p + .06);
        });

        var soft = true;
        void Style()
        {
            caption.Background = soft ? Frozen(Color.FromArgb(0x66, 0x0A, 0x14, 0x1E)) : Brushes.Transparent;
            caption.BorderBrush = soft ? Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) : null;
            caption.BorderThickness = new Thickness(soft ? 1 : 0);
        }
        Style();
        canvas.Children.Add(caption);
        canvas.Loaded += (_, _) =>
        {
            caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(caption, (canvas.ActualWidth - caption.DesiredSize.Width) / 2);
            Canvas.SetTop(caption, canvas.ActualHeight * .62);
        };
        Point? grab = null;
        caption.MouseLeftButtonDown += (_, e) => { grab = e.GetPosition(caption); caption.CaptureMouse(); };
        caption.MouseMove += (_, e) =>
        {
            if (grab is not { } g) return;
            var p = e.GetPosition(canvas);
            Canvas.SetLeft(caption, Math.Clamp(p.X - g.X, 0, Math.Max(0, canvas.ActualWidth - caption.ActualWidth)));
            Canvas.SetTop(caption, Math.Clamp(p.Y - g.Y, 0, Math.Max(0, canvas.ActualHeight - caption.ActualHeight)));
        };
        caption.MouseLeftButtonUp += (_, _) => { grab = null; caption.ReleaseMouseCapture(); };

        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 14, 0) };
        Button softB = null!, clearB = null!;
        softB = Segment(T("MinimalSoft"), () => { soft = true; Style(); Select(softB, true); Select(clearB, false); });
        clearB = Segment(T("MinimalClear"), () => { soft = false; Style(); Select(softB, false); Select(clearB, true); });
        Select(softB, true);
        tools.Children.Add(softB);
        tools.Children.Add(clearB);
        var size = new Slider { Minimum = 22, Maximum = 44, Value = 30, Width = 110, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("MinimalSize") };
        if (Application.Current.TryFindResource("SettingsSlider") is Style slider) size.Style = slider;
        size.ValueChanged += (_, e) => lyric.FontSize = e.NewValue;
        tools.Children.Add(size);
        var hint = new TextBlock { Text = T("MinimalDrag"), FontSize = 12.5, Foreground = CreamBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 14) };
        hint.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = .8, Color = Colors.Black };
        var host = new Grid();
        host.Children.Add(screen);
        var overlay = new Grid { Margin = new Thickness(36, 0, 0, 0) };
        overlay.Children.Add(tools);
        overlay.Children.Add(hint);
        host.Children.Add(overlay);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        return grid;
    }

    // ---- 07 Lyrics Studio ------------------------------------------------------------------------

    private static readonly DropShadowEffect FrontShadow = Shadow();

    private static DropShadowEffect Shadow()
    {
        var effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = .45, Color = Colors.Black };
        effect.Freeze();
        return effect;
    }

    private FrameworkElement BuildStudio()
    {
        var step = Current("Studio");
        var grid = Columns(360);
        var left = Stack(Eyebrow(T("StudioEyebrow")), Headline(T("StudioTitle"), 36), Body(T("StudioBody"), 15, top: 10));
        var flow = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
        var stepsText = new[] { T("StudioChoose"), T("StudioPick"), T("StudioMake"), T("StudioShare") };
        for (var i = 0; i < stepsText.Length; i++)
        {
            flow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 0, 8),
                Background = Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = stepsText[i], FontSize = 13, FontWeight = FontWeights.SemiBold },
            });
            if (i < stepsText.Length - 1)
                flow.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, Foreground = Faint, Margin = new Thickness(8, 9, 8, 0) });
        }
        left.Children.Add(flow);
        grid.Children.Add(left);
        Panel.SetZIndex(left, 10);

        // Cards the Lyrics Studio renderer itself makes, from the demonstration songs.
        var styles = new (LyricCardStyle Style, int Song)[]
        {
            (LyricCardStyle.Paint98, 0), (LyricCardStyle.LyricReceipt, 0), (LyricCardStyle.CassetteTape, 1), (LyricCardStyle.Polaroid, 2),
            (LyricCardStyle.GameBoy, 3), (LyricCardStyle.FilmNegative, 4),
        };
        var fan = new Grid { Margin = new Thickness(40, -20, 0, -20), ClipToBounds = true };
        var cards = new List<(Border Card, RotateTransform Turn, TranslateTransform Move, ScaleTransform Size)>();
        foreach (var (style, songIndex) in styles)
        {
            var song = TourSong.All[songIndex];
            var lines = song.Lines.Skip(2).Take(2).Select((l, i) => new LyricCardLine(l, TimeSpan.FromSeconds((i + 2) * TourSong.LineSeconds))).ToArray();
            var content = new LyricCardContent(song.Title, TourSong.Artist, song.Album, lines, song.Cover(480), song.End, song.Start, song.End, TimeSpan.FromSeconds(song.Duration));
            BitmapSource? image = null;
            try { image = LyricCardRenderer.Render(content, new LyricCardOptions { Style = style, Format = LyricCardFormat.Story }, .34); }
            catch (Exception ex) { Log.Error($"Tour card {style}", ex); }
            if (image is null) continue;
            var turn = new RotateTransform();
            var move = new TranslateTransform();
            var size = new ScaleTransform(1, 1);
            var card = new Border
            {
                Width = 216, Height = 384, CornerRadius = new CornerRadius(14), ClipToBounds = true,
                Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill },
                RenderTransformOrigin = new Point(.5, .9), RenderTransform = new TransformGroup { Children = { size, turn, move } },
                BorderBrush = Frozen(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = style.ToString(),
            };
            cards.Add((card, turn, move, size));
            fan.Children.Add(card);
        }
        var front = 0;
        void Arrange(bool animate)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                var offset = i - front;
                if (offset > cards.Count / 2) offset -= cards.Count;
                if (offset < -cards.Count / 2) offset += cards.Count;
                var (card, turn, move, size) = cards[i];
                Panel.SetZIndex(card, 100 - Math.Abs(offset) * 10);
                card.Effect = offset == 0 ? FrontShadow : null;
                var ms = animate ? 520 : 0;
                Animate(turn, RotateTransform.AngleProperty, offset * 7, ms);
                Animate(move, TranslateTransform.XProperty, offset * 84, ms);
                Animate(move, TranslateTransform.YProperty, Math.Abs(offset) * 22, ms);
                Animate(size, ScaleTransform.ScaleXProperty, offset == 0 ? 1.04 : .9, ms);
                Animate(size, ScaleTransform.ScaleYProperty, offset == 0 ? 1.04 : .9, ms);
                Fade(card, Math.Abs(offset) > 2 ? 0 : 1, ms);
            }
        }
        for (var i = 0; i < cards.Count; i++)
        {
            var index = i;
            cards[i].Card.MouseLeftButtonUp += (_, _) => { front = index; Arrange(true); };
        }
        Arrange(false);
        Every(step, 3200, () => { front = (front + 1) % Math.Max(1, cards.Count); Arrange(true); });
        Grid.SetColumn(fan, 1);
        grid.Children.Add(fan);
        return grid;
    }

    // ---- 08 Privacy --------------------------------------------------------------------------------

    private FrameworkElement BuildPrivacy()
    {
        var grid = Columns(560);
        var left = Stack(Eyebrow(T("PrivacyEyebrow")), Headline(T("PrivacyTitle"), 42),
            Body(T("PrivacyNo"), 19, top: 16, brush: CreamBrush),
            Body(T("PrivacyWhy"), 15, top: 12),
            Body(T("PrivacyOnline"), 13.5, top: 14),
            Body(T("PrivacyClose"), 16, top: 14, brush: CreamBrush));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var source = Ghost("  " + T("PrivacySource"), () => Open(ContactInfo.Repository));
        links.Children.Add(source);
        var mail = Ghost("  " + T("PrivacyWrite"), () => Open(ContactInfo.MailTo));
        mail.Margin = new Thickness(10, 0, 0, 0);
        mail.ToolTip = T("ContactHint");
        links.Children.Add(mail);
        left.Children.Add(links);
        grid.Children.Add(left);

        var values = new StackPanel { Margin = new Thickness(56, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (glyph, key) in new[] { ("\uE9D9", "ValueTelemetry"), ("\uE890", "ValueTracking"), ("\uE77B", "ValueAccount"), ("\uEB51", "ValueFree"), ("\uE943", "ValueOpen"), ("\uE8A5", "ValueMit") })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 16, Foreground = Turquoise, Width = 30, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = T(key), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            values.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(16, 11, 18, 12), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Card, BorderBrush = CardLine, BorderThickness = new Thickness(1), Child = row,
            });
        }
        Grid.SetColumn(values, 1);
        grid.Children.Add(values);
        return grid;
    }

    // ---- 09 Community Soundtrack ----------------------------------------------------------------

    private FrameworkElement BuildCommunity()
    {
        var grid = Columns(500);
        var left = Stack(Eyebrow(T("CommunityEyebrow")), Headline(T("CommunityTitle"), 40), Body(T("CommunityBody"), 16, top: 14),
            Body(T("CommunityNote"), 13.5, top: 12));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        var suggest = Ghost("\uE710  " + T("CommunitySuggest"), () => OfficialLinks.Open(OfficialLinks.SuggestSong));
        buttons.Children.Add(suggest);
        left.Children.Add(buttons);
        grid.Children.Add(left);

        // A still mosaic of covers, as a playlist made by many people would look. No animation.
        var mosaic = new UniformGrid { Columns = 3, Rows = 3, Width = 390, Height = 390, Margin = new Thickness(60, 0, 0, 0) };
        var tints = new[] { "#2B4A5E", "#5E3A4F", "#3E5A3A", "#6B5330" };
        for (var i = 0; i < 9; i++)
        {
            var tile = new Border { Margin = new Thickness(6), CornerRadius = new CornerRadius(10), ClipToBounds = true };
            if (i % 2 == 0)
                tile.Background = new ImageBrush(TourSong.All[i / 2 % TourSong.All.Length].Cover(256)) { Stretch = Stretch.UniformToFill };
            else
            {
                tile.Background = Frozen((Color)ColorConverter.ConvertFromString(tints[i / 2 % tints.Length]));
                tile.Child = new TextBlock { Text = "\uE8D6", FontFamily = Icons, FontSize = 26, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            mosaic.Children.Add(tile);
        }
        Grid.SetColumn(mosaic, 1);
        grid.Children.Add(mosaic);
        return grid;
    }

    // ---- 10 Make yourself at home ----------------------------------------------------------------

    private FrameworkElement BuildHome()
    {
        var root = new Grid();
        // Four covers in the corners, still and quiet, away from the words and buttons.
        var covers = new Canvas { IsHitTestVisible = false };
        var spots = new[] { (0, 10.0, 20.0, -8.0), (1, 900.0, 0.0, 7.0), (3, 0.0, 380.0, 6.0), (2, 900.0, 380.0, -5.0) };
        foreach (var (song, x, y, angle) in spots)
        {
            var cover = new Border
            {
                Width = 96, Height = 96, CornerRadius = new CornerRadius(12), Opacity = .6,
                Background = new ImageBrush(TourSong.All[song].Cover(192)), RenderTransform = new RotateTransform(angle, 48, 48),
            };
            Canvas.SetLeft(cover, x);
            Canvas.SetTop(cover, y);
            covers.Children.Add(cover);
        }
        root.Children.Add(covers);

        var gold = Frozen(Color.FromRgb(0xE2, 0xC4, 0x6F));
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 700 };
        var eyebrow = Eyebrow(T("HomeEyebrow2"));
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(eyebrow);
        var title = Headline(T("HomeTitle"), 56);
        title.TextAlignment = TextAlignment.Center;
        center.Children.Add(title);
        center.Children.Add(Body(T("HomeThanks2"), 16, top: 10, brush: CreamBrush, center: true));
        center.Children.Add(Body(T("HomeHope"), 16, top: 2, brush: CreamBrush, center: true));
        center.Children.Add(new TextBlock { Text = T("HomeSignature"), FontSize = 14, FontStyle = FontStyles.Italic, Foreground = Muted, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center });
        center.Children.Add(new Border { Width = 56, Height = 1, Background = SurfaceLine, Margin = new Thickness(0, 22, 0, 22), HorizontalAlignment = HorizontalAlignment.Center });

        StackPanel Offer(string glyph, Brush accent, string heading, string text, Button action)
        {
            var offer = new StackPanel { Width = 280, HorizontalAlignment = HorizontalAlignment.Center };
            offer.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 24, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center });
            offer.Children.Add(new TextBlock { Text = heading, FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 19, Margin = new Thickness(0, 10, 0, 0), TextAlignment = TextAlignment.Center });
            offer.Children.Add(Body(text, 13.5, top: 4, center: true));
            action.Margin = new Thickness(0, 14, 0, 0);
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.BorderBrush = accent;
            action.Background = Frozen(Color.FromArgb(0x18, ((SolidColorBrush)accent).Color.R, ((SolidColorBrush)accent).Color.G, ((SolidColorBrush)accent).Color.B));
            offer.Children.Add(action);
            return offer;
        }
        var offers = new Grid();
        offers.ColumnDefinitions.Add(new ColumnDefinition());
        offers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        offers.ColumnDefinitions.Add(new ColumnDefinition());
        var star = Offer("\uE735", gold, T("HomeFavor"), T("HomeStarShort"), Ghost("\uE735  " + T("HomeGitHub"), () => Open(ContactInfo.Repository)));
        var playlist = Offer("\uE7F6", Turquoise, T("HomeDiscover"), T("HomeDiscoverBody"), Ghost("\uE8D6  " + T("CommunityListen"), () => OfficialLinks.Open(OfficialLinks.Playlist)));
        star.Margin = new Thickness(0, 0, 32, 0);
        playlist.Margin = new Thickness(32, 0, 0, 0);
        offers.Children.Add(star);
        var line = new Border { Background = SurfaceLine, Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetColumn(line, 1);
        offers.Children.Add(line);
        Grid.SetColumn(playlist, 2);
        offers.Children.Add(playlist);
        center.Children.Add(offers);

        var start = Primary(T("HomeStart"), Close);
        start.Margin = new Thickness(0, 26, 0, 0);
        start.MinWidth = 200;
        start.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(start);
        root.Children.Add(center);
        return root;
    }

    // ---- Building blocks -----------------------------------------------------------------------

    private static Grid Columns(double left)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
            stack.Children.Add(child);
        return stack;
    }

    private static TextBlock Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Turquoise, Margin = new Thickness(0, 0, 0, 10),
    };

    private static TextBlock Headline(string text, double size) => new()
    {
        Text = text, FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.08,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    private static TextBlock Body(string text, double size, double top = 0, Brush? brush = null, bool center = false) => new()
    {
        Text = text, FontSize = size, Foreground = brush ?? Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
        LineHeight = size * 1.45, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
    };

    private static Button Make(string text, Action click, Brush background, Brush foreground, Brush? border, double fontSize = 15, Thickness? padding = null, FontWeight? weight = null, double radius = 10)
    {
        var label = IconText(text);
        var button = new Button
        {
            Content = label, Background = background, Foreground = foreground, BorderBrush = border ?? Brushes.Transparent, BorderThickness = new Thickness(border is null ? 0 : 1),
            Padding = padding ?? new Thickness(22, 10, 22, 11), FontSize = fontSize, FontWeight = weight ?? FontWeights.SemiBold, Cursor = Cursors.Hand,
            Template = ButtonTemplate(radius),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, text.TrimStart('', '', '', ' '));
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Text that may start with an icon glyph (private use area), drawn in the icon font.</summary>
    private static TextBlock IconText(string text)
    {
        var block = new TextBlock();
        if (text.Length > 0 && text[0] >= '' && text[0] <= '')
        {
            block.Inlines.Add(new System.Windows.Documents.Run(text[..1]) { FontFamily = Icons, BaselineAlignment = BaselineAlignment.Center });
            text = text[1..];
        }
        block.Inlines.Add(new System.Windows.Documents.Run(text));
        return block;
    }

    private static ControlTemplate ButtonTemplate(double radius)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        // A light layer over the fill: lighter on hover, darker on press.
        var root = new FrameworkElementFactory(typeof(Grid));
        var shine = new FrameworkElementFactory(typeof(Border), "Shine");
        shine.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        shine.SetValue(Border.BackgroundProperty, Brushes.White);
        shine.SetValue(UIElement.OpacityProperty, 0.0);
        shine.SetValue(UIElement.IsHitTestVisibleProperty, false);
        border.AppendChild(presenter);
        root.AppendChild(border);
        root.AppendChild(shine);
        template.VisualTree = root;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .14, "Shine"));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Turquoise, "Chrome"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .22, "Shine"));
        pressed.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(.97, .97)));
        pressed.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(.5, .5)));
        template.Triggers.Add(hover);
        template.Triggers.Add(focus);
        template.Triggers.Add(pressed);
        return template;
    }

    private static ControlTemplate CoverButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.ClipToBoundsProperty, true);
        border.SetValue(Border.BorderBrushProperty, Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        return template;
    }

    private static Button Primary(string text, Action click)
    {
        var button = Make(text, click, CreamBrush, Frozen(Color.FromRgb(0x10, 0x20, 0x2A)), null, 15.5, new Thickness(26, 12, 26, 13));
        button.Effect = PrimaryShadow;
        return button;
    }

    private static readonly DropShadowEffect PrimaryShadow = Glow();

    private static DropShadowEffect Glow()
    {
        var effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = .35, Color = Colors.Black };
        effect.Freeze();
        return effect;
    }

    private static readonly Brush Surface = Frozen(Color.FromRgb(0x1E, 0x2E, 0x3A));
    private static readonly Brush SurfaceLine = Frozen(Color.FromRgb(0x46, 0x5C, 0x6B));
    private static readonly Brush Card = Frozen(Color.FromRgb(0x15, 0x23, 0x2D));
    private static readonly Brush CardLine = Frozen(Color.FromRgb(0x2A, 0x3B, 0x47));

    private static Button Ghost(string text, Action click) => Make(text, click, Surface, CreamBrush, SurfaceLine);

    private static Button Link(string text, Action click) =>
        Make(text, click, Brushes.Transparent, CreamBrush, null, 13.5, new Thickness(12, 7, 12, 8), FontWeights.SemiBold, radius: 8);

    private static Button Segment(string text, Action click) =>
        Make(text, click, Surface, Muted, SurfaceLine, 13.5, new Thickness(14, 7, 14, 8), radius: 16);

    private static void Select(Button button, bool on)
    {
        button.Background = on ? CreamBrush : Surface;
        button.Foreground = on ? Frozen(Color.FromRgb(0x10, 0x20, 0x2A)) : CreamBrush;
        button.BorderBrush = on ? CreamBrush : SurfaceLine;
    }

    private static Button Chip(string text, Func<bool> isOn, Action<bool> set)
    {
        Button chip = null!;
        var label = (TextBlock)null!;
        var check = new System.Windows.Documents.Run("\uE73E  ") { FontFamily = Icons, FontSize = 11 };
        chip = Segment(text, () => { set(!isOn()); Mark(); });
        label = (TextBlock)chip.Content;
        void Mark()
        {
            Select(chip, isOn());
            if (isOn() && !label.Inlines.Contains(check)) label.Inlines.InsertBefore(label.Inlines.FirstInline, check);
            if (!isOn()) label.Inlines.Remove(check);
        }
        chip.Margin = new Thickness(4);
        Mark();
        return chip;
    }

    /// <summary>The taskbar player, copied into one reused bitmap 15 times a second (it is small and cheap to draw).</summary>
    private Image LiveImage(Step step, Func<BitmapSource?> source, double height)
    {
        var image = new Image { Stretch = Stretch.Uniform, Height = height, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Every(step, 66, () =>
        {
            try
            {
                var next = source();
                if (!ReferenceEquals(image.Source, next))
                    image.Source = next;
                image.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) { Log.Error("Tour preview", ex); }
        });
        return image;
    }

    /// <summary>
    /// A live view of one of the real players: a VisualBrush of its content, composed by the GPU as
    /// the player animates. Nothing is copied per frame; a slow timer only follows its size.
    /// </summary>
    private FrameworkElement Live(Step step, Func<FrameworkElement?> source, double width, double height = double.NaN)
    {
        var view = new Rectangle { Width = width, Height = height }; // a Rectangle with Uniform stretch draws a square
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        FrameworkElement? shown = null;
        var brush = new VisualBrush { Stretch = Stretch.Uniform };
        view.Fill = brush;
        void Follow()
        {
            try
            {
                var visual = source();
                if (!ReferenceEquals(visual, shown))
                {
                    shown = visual;
                    brush.Visual = visual;
                }
                view.Visibility = visual is null ? Visibility.Collapsed : Visibility.Visible;
                if (visual is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    // Frame the player's own box, not everything it draws (its scrolling text runs past it).
                    var box = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
                    if (brush.Viewbox != box)
                    {
                        brush.Viewbox = box;
                        brush.ViewboxUnits = BrushMappingMode.Absolute;
                    }
                    // Keep the view in the player's proportions (it changes width with its layout).
                    if (double.IsNaN(width) && !double.IsNaN(height))
                        view.Width = height * visual.ActualWidth / visual.ActualHeight;
                    else if (!double.IsNaN(width) && double.IsNaN(height))
                        view.Height = width * visual.ActualHeight / visual.ActualWidth;
                    else if (double.IsNaN(width) && double.IsNaN(height))
                    {
                        var fit = Math.Min((double.IsInfinity(view.MaxWidth) ? 560 : view.MaxWidth) / visual.ActualWidth,
                            (double.IsInfinity(view.MaxHeight) ? 480 : view.MaxHeight) / visual.ActualHeight);
                        view.Width = visual.ActualWidth * fit;
                        view.Height = visual.ActualHeight * fit;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Tour preview", ex); }
        }
        Every(step, 200, Follow);
        view.Loaded += (_, _) => Follow();
        return view;
    }

    /// <summary>
    /// A dark base lit by large, soft radial lights. Everything is clipped to the design rectangle, so
    /// the brush's bounds are exactly that rectangle (lights reaching past it used to enlarge the
    /// drawing and squeeze the gradient into a visible box).
    /// </summary>
    private static Brush Atmosphere(string from, string middle, string to)
    {
        var c = (string h) => (Color)ColorConverter.ConvertFromString(h);
        var (dark, light, warm) = (c(from), c(middle), c(to));
        var rect = new Rect(0, 0, DesignWidth, DesignHeight);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(rect));
            dc.DrawRectangle(Frozen(dark), null, rect);
            Light(dc, light, 0xD0, new Point(820, 200), 760, 560);
            Light(dc, warm, 0x90, new Point(1060, 700), 640, 460);
            Light(dc, Color.FromRgb(0x18, 0x9B, 0xB3), 0x26, new Point(560, 360), 620, 420);
            Light(dc, Color.FromRgb(0x04, 0x0A, 0x10), 0x90, new Point(40, 40), 560, 460);
            dc.Pop();
        }
        group.Freeze();
        return Baked(group, Stretch.Fill);
    }

    /// <summary>The drawing rendered once to a bitmap: crossfades and resizing then cost a texture, not gradients.</summary>
    private static Brush Baked(DrawingGroup drawing, Stretch stretch)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawDrawing(drawing);
        var bitmap = new RenderTargetBitmap((int)(DesignWidth * 1.5), (int)(DesignHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = stretch };
        brush.Freeze();
        return brush;
    }

    /// <summary>A radial light that is fully transparent well before its ellipse ends.</summary>
    private static void Light(DrawingContext dc, Color color, byte alpha, Point centre, double rx, double ry)
    {
        var brush = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
            new(Color.FromArgb((byte)(alpha * .55), color.R, color.G, color.B), .35),
            new(Color.FromArgb((byte)(alpha * .18), color.R, color.G, color.B), .7),
            new(Color.FromArgb(0, color.R, color.G, color.B), 1),
        });
        brush.Freeze();
        dc.DrawEllipse(brush, null, centre, rx, ry);
    }

    private static Brush Photo(string name, double darken)
    {
        var rect = new Rect(0, 0, DesignWidth, DesignHeight);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(rect));
            dc.DrawRectangle(new ImageBrush(Load(name)) { Stretch = Stretch.UniformToFill }, null, rect);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb((byte)Math.Min(255, 255 * darken + 60), 8, 14, 24), Color.FromArgb((byte)(255 * darken), 8, 14, 24), 0), null, rect);
            dc.Pop();
        }
        group.Freeze();
        return Baked(group, Stretch.UniformToFill);
    }

    private static Brush PhotoBrush(string name, double darken, Stretch stretch)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var rect = new Rect(0, 0, 1600, 1000);
            dc.DrawRectangle(new ImageBrush(Load(name)) { Stretch = stretch }, null, rect);
            if (darken > 0)
                dc.DrawRectangle(Frozen(Color.FromArgb((byte)(255 * darken), 8, 14, 24)), null, rect);
        }
        group.Freeze();
        return new DrawingBrush(group) { Stretch = Stretch.UniformToFill };
    }

    private static readonly Dictionary<string, BitmapImage> Photos = [];

    private static BitmapImage Load(string name)
    {
        if (Photos.TryGetValue(name, out var cached))
            return cached;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Taskitify;component/Resources/Tour/{name}");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return Photos[name] = image;
    }

    private static void AutomationProperties(FrameworkElement element, string name) =>
        System.Windows.Automation.AutomationProperties.SetHelpText(element, name);

    private static bool AnimationsOn => SystemParameters.ClientAreaAnimation;

    private static void Fade(UIElement element, double to, double ms)
    {
        if (!AnimationsOn || ms <= 0)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, double ms)
    {
        if (!AnimationsOn || ms <= 0)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"Could not open {url}", ex); }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
