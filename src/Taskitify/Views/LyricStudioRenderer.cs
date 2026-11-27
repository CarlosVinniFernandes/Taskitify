using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Taskitify.Models;

namespace Taskitify.Views;

public static partial class LyricCardRenderer
{
    private static readonly FontFamily Pixel = new(FontFolder, "./#Pixelify Sans");
    private static readonly Face PixelFace = new(Pixel, FontWeights.Normal, FontStyles.Normal, FontStretches.Normal, .96, 1.1);
    private static readonly Face TerminalFace = new(Mono, FontWeights.SemiBold, FontStyles.Normal, FontStretches.Normal, .94, 1.21);
    private static readonly Face DisplayFace = new(InterDisplay, FontWeights.Bold, FontStyles.Normal, FontStretches.Normal, 1, 1.12);
    private static readonly Color Paper = C("#F3E9D5");
    private static readonly Color Ink = C("#252320");

    private static FrameworkElement ComposeStudio(Ctx ctx)
    {
        var d = new Studio(ctx);
        return ctx.O.Style switch
        {
            LyricCardStyle.PhotoBooth => PhotoBooth(d),
            LyricCardStyle.Letter => PersonalLetter(d),
            LyricCardStyle.FilmNegative => FilmNegative(d),
            LyricCardStyle.Telegram => Telegram(d),
            LyricCardStyle.MuseumExhibit => Museum(d),
            LyricCardStyle.MsnMessenger => Messenger(d),
            LyricCardStyle.WindowsXpError => ErrorDialog(d),
            LyricCardStyle.FloppyDisk => Floppy(d),
            LyricCardStyle.Terminal => Terminal(d),
            LyricCardStyle.BlueScreen => BlueScreen(d),
            LyricCardStyle.AimAway => Away(d),
            LyricCardStyle.Calculator => Calculator(d),
            LyricCardStyle.Paint98 => Paint(d),
            LyricCardStyle.Orkut => Profile(d),
            LyricCardStyle.IpodClassic => PocketPlayer(d),
            LyricCardStyle.CassetteTape => Cassette(d),
            LyricCardStyle.ConcertTicket => ConcertTicket(d),
            LyricCardStyle.LyricReceipt => Receipt(d),
            LyricCardStyle.BoardingPass => BoardingPassTicket(d),
            LyricCardStyle.LibraryCard => Library(d),
            LyricCardStyle.MusicPassport => Passport(d),
            LyricCardStyle.ClassifiedDocument => Classified(d),
            LyricCardStyle.Dictionary => DictionaryEntry(d),
            LyricCardStyle.BookPage => Book(d),
            LyricCardStyle.PeriodicTable => ElementTile(d),
            LyricCardStyle.Crossword => Crossword(d),
            LyricCardStyle.GameBoy => PocketConsole(d),
            LyricCardStyle.NintendoDs => DualScreen(d),
            LyricCardStyle.PokemonEncounter => Encounter(d),
            LyricCardStyle.RpgInventory => Inventory(d),
            LyricCardStyle.MagicCard => FantasyCard(d),
            LyricCardStyle.Chess => Chess(d),
            _ => Classic(ctx),
        };
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children)
            stack.Children.Add(child);
        return stack;
    }

    private static Border Box(UIElement child, Color color, double pad = 0, double radius = 0, Color? stroke = null) => new()
    {
        Child = child, Background = Brush(color), Padding = new Thickness(pad), CornerRadius = new CornerRadius(radius),
        BorderThickness = new Thickness(stroke is null ? 0 : 2), BorderBrush = stroke is { } ink ? Brush(ink) : null,
    };

    private static T Space<T>(T item, double top, double bottom = 0) where T : FrameworkElement
    {
        item.Margin = new Thickness(0, top, 0, bottom);
        return item;
    }

    private static FrameworkElement Pair(FrameworkElement left, FrameworkElement right, double fraction = .5, double gap = 24)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        row.Children.Add(left);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);
        return row;
    }

    private static FrameworkElement Rule(Color color, bool dotted = false, double weight = 2) => new Line
    {
        Stroke = Brush(color), StrokeThickness = weight, X2 = 1, Stretch = Stretch.Fill,
        StrokeDashArray = dotted ? new DoubleCollection([6, 4]) : null, Height = weight + 1,
    };

    private static FrameworkElement Keys(string[] names, int columns, double height, Color bg, Color fg, FontFamily? font = null)
    {
        var grid = new UniformGrid { Columns = columns };
        foreach (var name in names)
        {
            var key = Box(Label(name, font ?? Figtree, Math.Min(height * .47, 28), FontWeights.Bold, fg, TextAlignment.Center),
                bg, 0, Math.Min(9, height * .14));
            key.Height = height;
            key.Margin = new Thickness(3);
            ((FrameworkElement)key.Child).VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(key);
        }
        return grid;
    }

    private static FrameworkElement Barcode(Color fg, double height)
    {
        var grid = new UniformGrid { Rows = 1, Height = height, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var n in "341724315462124573164342645321734613546")
            grid.Children.Add(new Rectangle { Fill = Brush(fg), Width = 2 + (n - '0') * .88, Margin = new Thickness(1, 0, 1, 0) });
        return grid;
    }

    private static FrameworkElement Stamp(string value, Color color, double size = 25) => new Border
    {
        BorderBrush = Brush(color), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(16, 7, 16, 7), HorizontalAlignment = HorizontalAlignment.Left,
        LayoutTransform = new RotateTransform(-6, .5, .5),
        Child = Label(value.ToUpperInvariant(), Mono, size, FontWeights.Bold, color, TextAlignment.Center),
    };

    private static FrameworkElement Artwork(double height, Color hue, int motif)
    {
        var canvas = new Canvas { Width = 460, Height = 260, ClipToBounds = true, Background = Brush(Darken(hue, .32)) };
        for (var i = 0; i < 8; i++)
        {
            var ring = new Ellipse
            {
                Width = 46 + i * 44, Height = 46 + i * 44, StrokeThickness = 2,
                Stroke = Brush(Alpha(Lighten(hue, .6), (byte)(155 - i * 12))),
            };
            Canvas.SetLeft(ring, 230 - ring.Width / 2); Canvas.SetTop(ring, 130 - ring.Height / 2);
            canvas.Children.Add(ring);
        }
        var shape = new Polygon
        {
            Fill = new LinearGradientBrush(Lighten(hue, .75), hue, 65),
            Points = motif % 2 == 0 ? new PointCollection([new(230, 30), new(298, 115), new(230, 222), new(161, 115)])
                : new PointCollection([new(100, 190), new(218, 42), new(248, 120), new(329, 74), new(363, 194)]),
        };
        canvas.Children.Add(shape);
        return new Viewbox { Height = height, Stretch = Stretch.Uniform, Child = canvas };
    }

    private static Brush Desk(Color light, Color deep) => new RadialGradientBrush(light, deep)
    {
        Center = new Point(.37, .28), GradientOrigin = new Point(.29, .16), RadiusX = .87, RadiusY = .77,
    };

    private static Border Enamel(UIElement child, Color color, Thickness pad, CornerRadius corners)
    {
        return new Border
        {
            Child = child, Padding = pad, CornerRadius = corners,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Lighten(color, .24), 0), new(color, .42), new(Darken(color, .76), 1)
            }, 116),
            BorderThickness = new Thickness(2.5, 2, 5, 7),
            BorderBrush = new LinearGradientBrush(Lighten(color, .48), Darken(color, .44), 117),
        };
    }

    private static Border Recess(UIElement child, Color color, double pad, double radius) => new()
    {
        Child = child, Padding = new Thickness(pad), CornerRadius = new CornerRadius(radius),
        Background = new LinearGradientBrush(Darken(color, .85), color, 88),
        BorderThickness = new Thickness(3, 5, 2, 2),
        BorderBrush = new LinearGradientBrush(Darken(color, .45), Lighten(color, .26), 100),
    };

    private static FrameworkElement Screw(Color metal, double size)
    {
        var screw = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        screw.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Lighten(metal, .5), Darken(metal, .43), 80),
            Stroke = Brush(Darken(metal, .38)), StrokeThickness = 1 });
        screw.Children.Add(new Border { Width = size * .52, Height = size * .12, Background = Brush(Darken(metal, .36)),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-29) });
        return screw;
    }

    private static FrameworkElement Vents(Color color, int count, double width, double height)
    {
        var vents = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < count; i++)
            vents.Children.Add(new Border { Width = width, Height = height, Margin = new Thickness(width * .6, 0, 0, 0),
                Background = Brush(color), CornerRadius = new CornerRadius(width / 2), RenderTransform = new SkewTransform(-17, 0) });
        return vents;
    }

    private static Border Mounted(Border item, double shadow = 22)
    {
        item.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = shadow * 1.9, ShadowDepth = shadow * .55, Direction = 270,
            Opacity = .34, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
        return item;
    }

    private static FrameworkElement WindowBar(Studio d, string title, Color ink, Color left, Color right, string[] controls)
    {
        var caption = new DockPanel { LastChildFill = true };
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var label in controls)
        {
            var button = Box(d.Small(label, ink, 23, TextAlignment.Center), Alpha(Colors.White, 44), 0, 4, Alpha(ink, 66));
            button.Width = 40;
            button.Height = 36;
            button.Margin = new Thickness(4, 0, 0, 0);
            ((FrameworkElement)button.Child).VerticalAlignment = VerticalAlignment.Center;
            icons.Children.Add(button);
        }
        DockPanel.SetDock(icons, Dock.Right);
        caption.Children.Add(icons);
        var text = d.Small(title, ink, 29);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.MaxHeight = 37;
        caption.Children.Add(text);
        return new Border { Child = caption, Padding = new Thickness(17, 11, 10, 11), CornerRadius = new CornerRadius(7, 7, 0, 0),
            Background = new LinearGradientBrush(left, right, 3) };
    }

    private static Brush Fibers(Color paper, Color thread, double scale = 1)
    {
        var drawing = new DrawingGroup();
        var page = new Rect(0, 0, 1000, 1350);
        drawing.Children.Add(new GeometryDrawing(Brush(paper), null, new RectangleGeometry(page)));
        // Stable, non-repeating grain avoids visible tile seams at thumbnail scale.
        var random = new Random(761);
        var fibers = new GeometryGroup();
        for (var i = 0; i < 370; i++)
        {
            fibers.Children.Add(new EllipseGeometry(new Point(random.NextDouble() * page.Width, random.NextDouble() * page.Height),
                scale * (1 + random.NextDouble() * 2.4), scale * (.24 + random.NextDouble() * .49)));
        }
        drawing.Children.Add(new GeometryDrawing(Brush(thread), null, fibers));
        drawing.Freeze();
        var brush = new DrawingBrush(drawing);
        brush.Freeze();
        return brush;
    }

    private sealed class Studio
    {
        private readonly Ctx _ctx;
        private readonly StudioStyle _spec;
        private readonly LyricCardVariant? _variant;
        public Studio(Ctx ctx)
        {
            _ctx = ctx;
            _spec = LyricStudioCatalog.Get(ctx.O.Style);
            _variant = ctx.O.Variants?.GetValueOrDefault(ctx.O.Style);
            Width = Math.Min(ctx.W - 124, ctx.Landscape ? 1392 : 910);
        }

        public double Width { get; }
        public double CardWidth => _ctx.W;
        public double U => _ctx.U;

        /// <summary>The chosen lines as one text, one per line ("♪" when none is chosen).</summary>
        public string LyricText => _ctx.C.Lines.Count == 0 ? "♪" : string.Join("\n", _ctx.C.Lines.Select(line => line.Text));

        /// <summary>The typeface the user picked for the lines, or null to keep the style's own.</summary>
        public Face? UserFace => _ctx.O.Font == LyricCardFont.Auto ? null : _ctx.Face(LyricCardFont.Modern);

        /// <summary>The user's text size, as a factor.</summary>
        public double TextScale => Math.Clamp(_ctx.O.FontScale, 0.6, 1.5);

        /// <summary>The signature row (Spotify, Taskitify or none, and the timestamp).</summary>
        public FrameworkElement Signature(Color fg, Color bg, TextAlignment align = TextAlignment.Center) =>
            Footer(_ctx, fg, bg, align, new Thickness());

        public TextAlignment Align(TextAlignment fallback) => _ctx.Align(fallback);

        public bool Wide => _ctx.Landscape;
        public bool Story => _ctx.O.Format == LyricCardFormat.Story;
        public double Height => _ctx.H;
        public Color Accent => _ctx.C.Accent;
        public string Title => _ctx.C.Title;
        public string Artist => _ctx.C.Artist;
        public string? Album => _ctx.C.Album;
        public string Duration => _ctx.C.Duration is { } duration ? Clock(duration) : "—";
        public string Time => _ctx.FirstTime is { } time ? Clock(time) : "—";
        public string? Initial => _ctx.C.Lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line.Text)) is { } first
            ? StringInfo.GetNextTextElement(first.Text.TrimStart()) : null;
        public bool Timestamp => _ctx.O.ShowTimestamp && _ctx.FirstTime is not null;
        public double H(double storyUnits) => storyUnits * Math.Min(1, _ctx.H / 1550);
        public double Art(double storyUnits) => H(storyUnits) * _ctx.Art;

        public string Text(string key)
        {
            var value = _variant?.Fields?.GetValueOrDefault(key) ?? _spec.Fields.First(field => field.Key == key).Default;
            return value.Replace("{title}", Title).Replace("{artist}", Artist)
                .Replace("{date}", DateTime.Today.ToString("dd MMM yyyy", CultureInfo.CurrentCulture))
                .Replace("{time}", Time).Replace("{duration}", Duration);
        }

        public Color Color(string key, Color fallback)
        {
            var value = Text(key);
            return value.Length == 7 && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
                ? System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                : fallback;
        }

        public BitmapSource? Image => !_spec.Image ? null : (_variant?.ImageSource ?? StudioImageSource.Album) switch
        {
            StudioImageSource.Album => _ctx.C.Cover,
            StudioImageSource.Personal => _ctx.C.PersonalImage,
            _ => null,
        };

        public FrameworkElement? Photo(double height, double radius = 0, double? width = null)
        {
            if (Image is not { } image)
                return null;
            return new Border
            {
                Height = height, Width = width ?? double.NaN, CornerRadius = new CornerRadius(radius),
                Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill },
            };
        }

        public TextBlock Small(string text, Color ink, double size = 25, TextAlignment align = TextAlignment.Left, FontFamily? font = null)
            => CopyLabel(text, font ?? InterDisplay, size, ink, align, FontWeights.SemiBold);

        public TextBlock TitleText(string text, Color ink, double size = 62, FontFamily? font = null, TextAlignment align = TextAlignment.Left)
            => CopyLabel(text, font ?? InterDisplay, size, ink, align, font == DmSerif ? FontWeights.Normal : FontWeights.Bold);

        private static TextBlock CopyLabel(string text, FontFamily font, double size, Color ink, TextAlignment align, FontWeight weight)
        {
            var block = Label(text, font, size, weight, ink, align);
            block.TextWrapping = TextWrapping.Wrap;
            block.MaxHeight = size * 2.8;
            block.LineHeight = size * 1.15;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            return block;
        }

        public FrameworkElement Lyrics(double width, Color ink, double size = 67, TextAlignment align = TextAlignment.Left, Face? face = null, bool dropInitial = false)
        {
            var selected = face ?? (_ctx.O.Font == LyricCardFont.Auto && _spec.Font == LyricCardFont.Modern ? DisplayFace : null);
            return _ctx.Lines(_spec.Font, size, ink, _ctx.Align(align), Math.Max(width, 200), selected, dropInitial);
        }

        public FrameworkElement Progress(Color fill, Color track, double height)
        {
            var fraction = _ctx.FirstTime is { } time && _ctx.C.Duration is { TotalSeconds: > 0 } duration
                ? Math.Clamp(time.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
            var bar = new Grid { Height = height, Background = Brush(track) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            bar.Children.Add(new Rectangle { Fill = Brush(fill) });
            return bar;
        }

        public FrameworkElement Song(Color color, double size = 30) =>
            Small(string.IsNullOrWhiteSpace(Artist) ? Title : Title + "  ·  " + Artist, color, size);

        public FrameworkElement Stage(FrameworkElement content, Color background, Color foot, double? width = null,
            Brush? outer = null, bool top = false, double inset = 34, double footerGap = 38)
        {
            var stack = Stack(content, Space(Footer(_ctx, foot, background, TextAlignment.Center, new Thickness()), H(footerGap)));
            stack.Width = width ?? Width;
            stack.HorizontalAlignment = HorizontalAlignment.Center;
            stack.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
            stack.Margin = new Thickness(0, H(inset), 0, H(inset));
            _ctx.Fit(stack, _ctx.H - 8);
            var root = new Grid { Background = outer ?? Brush(background) };
            if (_ctx.O.Color is not null)
                root.Children.Add(new Border { Background = Brush(Alpha(Accent, 61)) });
            root.Children.Add(stack);
            return root;
        }
    }
}
