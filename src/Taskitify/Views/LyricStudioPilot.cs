using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Taskitify.Views;

/// <summary>
/// The pilot set of the Lyrics Studio's object styles, drawn as the objects themselves (real
/// proportions, period typography, physical details) rather than as panels with text: a pocket
/// console, a dictionary page, a personal letter and an airline boarding pass. Each object is
/// drawn at a fixed design size and scaled as a whole to the card, so its proportions hold in
/// every format; only its text adapts (it is fitted to the space the object gives it).
/// </summary>
public static partial class LyricCardRenderer
{
    private static readonly FontFamily Georgia = new("Georgia, Cambria, Times New Roman");
    private static readonly FontFamily Gill = new("Gill Sans MT, Segoe UI");
    private static readonly FontFamily Symbols = new("Segoe UI Symbol, Segoe UI");

    // ======================= Pocket console =======================

    /// <summary>
    /// A grey brick of a handheld from 1989: the off-white shell with its rounded lower-right
    /// corner, the slate bezel with the two-tone stripes and the battery light, the pea-green
    /// dot-matrix screen, the cross pad, the tilted A/B pair, the slanted start/select pills
    /// and the speaker slots.
    /// </summary>
    private static FrameworkElement PocketConsole(Studio d)
    {
        const double w = 900, h = 1480;
        var shell = d.Color("device", C("#D2CFC6"));
        var screen = d.Color("screen", C("#A7B26A"));
        var ink = Luminance(screen) > .45 ? C("#24361C") : C("#E9F1D8");
        var navy = C("#2C2E86");
        var plum = C("#9C2A58");
        var canvas = new Canvas { Width = w, Height = h };

        // Shell: square corners but the famous rounded lower right.
        var body = RoundRect(0, 0, w, h, 34, 34, 176, 34);
        canvas.Children.Add(new Path
        {
            Data = body,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Lighten(shell, .16), 0), new(shell, .35), new(Darken(shell, .9), 1),
            }, new Point(0, 0), new Point(.35, 1)),
            Stroke = Brush(Darken(shell, .72)), StrokeThickness = 3,
        });
        // A soft highlight along the left edge and a darker right flank: a moulded plastic body.
        canvas.Children.Add(new Path { Data = body, Fill = new LinearGradientBrush(Alpha(Colors.White, 40), Alpha(Colors.Black, 28), 0) });
        // The seam and the power switch slots along the top.
        canvas.Children.Add(At(new Rectangle { Width = w - 68, Height = 3, Fill = Brush(Darken(shell, .78)) }, 34, 70));
        canvas.Children.Add(At(new Rectangle { Width = w - 68, Height = 2, Fill = Brush(Alpha(Colors.White, 120)) }, 34, 73));
        foreach (var x in new[] { 150.0, 750.0 })
            canvas.Children.Add(At(new Rectangle { Width = 3, Height = 70, Fill = Brush(Darken(shell, .78)) }, x, 0));
        canvas.Children.Add(At(Label("◄ OFF · ON ►", Gill, 19, FontWeights.SemiBold, Darken(shell, .55), TextAlignment.Left), 172, 24));

        // Bezel: dark slate glass, its lower right rounded like the shell.
        var bezel = new Path
        {
            Data = RoundRect(58, 108, 784, 640, 22, 22, 96, 22),
            Fill = new LinearGradientBrush(C("#666879"), C("#4B4D5C"), 90),
            Stroke = Brush(C("#3B3D49")), StrokeThickness = 2,
        };
        canvas.Children.Add(bezel);
        // The two-tone stripes either side of the bezel's caption.
        canvas.Children.Add(At(new Rectangle { Width = 150, Height = 5, Fill = Brush(C("#8E3B6C")) }, 92, 135));
        canvas.Children.Add(At(new Rectangle { Width = 150, Height = 5, Fill = Brush(C("#2F3E86")) }, 92, 146));
        canvas.Children.Add(At(new Rectangle { Width = 150, Height = 5, Fill = Brush(C("#8E3B6C")) }, 658, 135));
        canvas.Children.Add(At(new Rectangle { Width = 150, Height = 5, Fill = Brush(C("#2F3E86")) }, 658, 146));
        var caption = Label("DOT MATRIX WITH STEREO SOUND", Gill, 21, FontWeights.SemiBold, C("#C9CAD4"), TextAlignment.Center);
        caption.Width = 400;
        caption.FontStyle = FontStyles.Italic;
        canvas.Children.Add(At(caption, 250, 126));
        // Battery light.
        canvas.Children.Add(At(new Ellipse
        {
            Width = 20, Height = 20,
            Fill = new RadialGradientBrush(C("#FF8A7A"), C("#C0221A")),
            Effect = new DropShadowEffect { Color = C("#FF3B2A"), BlurRadius = 18, ShadowDepth = 0, Opacity = .85 },
        }, 98, 348));
        canvas.Children.Add(At(Label("BATTERY", Gill, 15, FontWeights.SemiBold, C("#B9BBC6"), TextAlignment.Left), 80, 378));

        // Screen: a recessed dot-matrix panel.
        canvas.Children.Add(At(new Border { Width = 532, Height = 486, Background = Brush(C("#34353F")), CornerRadius = new CornerRadius(4) }, 184, 186));
        var lcd = new Grid
        {
            Width = 508, Height = 462,
            Background = new LinearGradientBrush(Darken(screen, .9), Lighten(screen, .08), new Point(0, 0), new Point(1, 1)),
        };
        lcd.Children.Add(new Border { Background = DotMatrix(ink) });
        lcd.Children.Add(new Border
        {
            BorderThickness = new Thickness(6, 6, 0, 0),
            BorderBrush = new LinearGradientBrush(Alpha(Colors.Black, 70), Alpha(Colors.Black, 0), 45),
        });
        var face = d.UserFace;
        var family = face?.Family ?? Mono;
        var weight = face?.Weight ?? FontWeights.Bold;
        var lyric = FitText(d.LyricText, family, weight, FontStyles.Normal, ink, 444, 282, 50 * d.TextScale, 18, d_Align(d, TextAlignment.Left), 1.22);
        var copy = new DockPanel { Margin = new Thickness(32, 28, 32, 26) };
        var note = Label("♪ " + d.Text("note").ToUpperInvariant(), Mono, 21, FontWeights.Bold, Alpha(ink, 200), TextAlignment.Left);
        DockPanel.SetDock(note, Dock.Top);
        copy.Children.Add(note);
        var song = Label("▶ " + (d.Title + "  " + d.Artist).ToUpperInvariant(), Mono, 18, FontWeights.Bold, Alpha(ink, 190), TextAlignment.Left);
        song.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(song, Dock.Bottom);
        copy.Children.Add(song);
        lyric.VerticalAlignment = VerticalAlignment.Center;
        copy.Children.Add(lyric);
        lcd.Children.Add(copy);
        canvas.Children.Add(At(lcd, 196, 198));

        // Brand line under the screen.
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(Label("Taskitify", Figtree, 38, FontWeights.ExtraBold, navy, TextAlignment.Left));
        var model = Label(" " + d.Text("label"), Gill, 46, FontWeights.SemiBold, navy, TextAlignment.Left);
        model.FontStyle = FontStyles.Italic;
        model.Margin = new Thickness(10, -8, 0, 0);
        brand.Children.Add(model);
        brand.Children.Add(Label("™", Gill, 16, FontWeights.Normal, navy, TextAlignment.Left));
        canvas.Children.Add(At(brand, 70, 778));

        // Cross pad in its round well.
        canvas.Children.Add(At(new Ellipse { Width = 250, Height = 250, Fill = new RadialGradientBrush(Darken(shell, .9), Alpha(shell, 0)) }, 80, 922));
        var pad = new Grid { Width = 210, Height = 210 };
        foreach (var (pw, ph) in new[] { (70.0, 210.0), (210.0, 70.0) })
            pad.Children.Add(new Border
            {
                Width = pw, Height = ph, CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(C("#3A3B44"), C("#1E1F25"), 90),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 5, Direction = 300, Opacity = .45 },
            });
        pad.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = new RadialGradientBrush(C("#2A2B32"), C("#17181C")) });
        foreach (var (glyph, x, y) in new[] { ("▲", 93.0, 12.0), ("▼", 93.0, 176.0), ("◀", 12.0, 92.0), ("▶", 178.0, 92.0) })
        {
            var arrow = Label(glyph, Symbols, 16, FontWeights.Normal, C("#55565F"), TextAlignment.Left);
            arrow.Margin = new Thickness(x, y, 0, 0);
            arrow.HorizontalAlignment = HorizontalAlignment.Left;
            arrow.VerticalAlignment = VerticalAlignment.Top;
            pad.Children.Add(arrow);
        }
        canvas.Children.Add(At(pad, 100, 942));

        // A and B in their tilted capsule.
        // The capsule follows the line through both buttons.
        var b = new Point(621, 1039);
        var a = new Point(765, 969);
        var axis = a - b;
        var angle = Math.Atan2(axis.Y, axis.X) * 180 / Math.PI;
        var length = axis.Length + 132;
        var centre = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var well = new Border
        {
            Width = length, Height = 128, CornerRadius = new CornerRadius(64),
            Background = Brush(Darken(shell, .87)), BorderBrush = Brush(Darken(shell, .78)), BorderThickness = new Thickness(2),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(angle),
        };
        canvas.Children.Add(At(well, centre.X - (length / 2), centre.Y - 64));
        foreach (var (letter, x, y) in new[] { ("B", b.X - 49, b.Y - 49), ("A", a.X - 49, a.Y - 49) })
        {
            canvas.Children.Add(At(new Ellipse
            {
                Width = 98, Height = 98,
                Fill = new RadialGradientBrush(Lighten(plum, .25), Darken(plum, .8)) { GradientOrigin = new Point(.35, .3) },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 6, Direction = 290, Opacity = .45 },
            }, x, y));
            var mark = Label(letter, Gill, 34, FontWeights.SemiBold, navy, TextAlignment.Left);
            mark.FontStyle = FontStyles.Italic;
            canvas.Children.Add(At(mark, x + 66, y + 104));
        }

        // Select and start: slanted rubber pills.
        foreach (var (name, x) in new[] { ("SELECT", 308.0), ("START", 438.0) })
        {
            canvas.Children.Add(At(new Border
            {
                Width = 104, Height = 30, CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Darken(shell, .82), Darken(shell, .92), 90),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-26),
            }, x - 6, 1236));
            canvas.Children.Add(At(new Border
            {
                Width = 88, Height = 20, CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(C("#9C9AA2"), C("#6E6C74"), 90),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-26),
            }, x + 2, 1241));
            var label = Label(name, Gill, 19, FontWeights.SemiBold, navy, TextAlignment.Left);
            label.FontStyle = FontStyles.Italic;
            canvas.Children.Add(At(label, x + 10, 1290));
        }

        // Speaker slots, slanted like the buttons.
        for (var i = 0; i < 6; i++)
        {
            canvas.Children.Add(At(new Border
            {
                Width = 20, Height = 160, CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(Darken(shell, .52), Darken(shell, .72), 0),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-28),
            }, 640 + (i * 36), 1238));
        }
        canvas.Children.Add(At(Label("● PHONES", Gill, 15, FontWeights.SemiBold, Darken(shell, .58), TextAlignment.Left), 380, 1440));

        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 70, ShadowDepth = 26, Direction = 280, Opacity = .45 };
        var backdrop = Desk(Lighten(Darken(d.Accent, .7), .18), Darken(d.Accent, .32));
        return ObjectStage(d, canvas, w, h, backdrop, Colors.White, Darken(d.Accent, .4));
    }

    /// <summary>The faint grid of a dot-matrix screen.</summary>
    private static Brush DotMatrix(Color ink)
    {
        var cell = new DrawingGroup();
        cell.Children.Add(new GeometryDrawing(Brush(Alpha(ink, 16)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        cell.Children.Add(new GeometryDrawing(Brush(Alpha(ink, 16)), null, new RectangleGeometry(new Rect(0, 0, 1, 4))));
        var brush = new DrawingBrush(cell) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 5, 5), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    // ======================= Dictionary page =======================

    /// <summary>
    /// A page of a dictionary, edge to edge: running head and guide word, the headword set large
    /// and alone, its grammar in italics, the lines as the numbered definition, the source as the
    /// citation, and a thumb-index tab cut into the page's edge.
    /// </summary>
    private static FrameworkElement DictionaryEntry(Studio d)
    {
        var u = d.U;
        var W = d.CardWidth;
        var H = d.Height;
        var paper = C("#F4F0E6");
        var ink = C("#1D1B18");
        var grey = C("#7A7166");
        var accent = Luminance(d.Accent) > .55 ? Darken(d.Accent, .5) : d.Accent;
        var word = d.Text("word");
        var root = new Grid { Background = Fibers(paper, Alpha(C("#B9A98E"), 34)) };
        var left = (d.Wide ? 190 : 118) * u;
        var right = (d.Wide ? 230 : 150) * u;
        var width = W - left - right;

        // Running head: dictionary name, rule, guide word and page.
        var head = new Grid { Margin = new Thickness(left, 84 * u, right, 0), VerticalAlignment = VerticalAlignment.Top };
        head.Children.Add(Spaced("TASKITIFY · DICTIONARY OF SONGS", Figtree, 20 * u, FontWeights.Bold, grey, TextAlignment.Left, .3));
        var guide = Label(word.ToLower(CultureInfo.CurrentCulture) + "   " + d.Text("number").PadLeft(1) + "27", Georgia, 24 * u, FontWeights.Normal, grey, TextAlignment.Right);
        guide.FontStyle = FontStyles.Italic;
        head.Children.Add(guide);
        root.Children.Add(head);
        root.Children.Add(new Border { Height = 1.6 * u, Background = Brush(Alpha(ink, 150)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, 128 * u, right, 0) });

        // Thumb index: the page's letter, cut into the right edge.
        var initial = string.IsNullOrWhiteSpace(word) ? "A" : StringInfo.GetNextTextElement(word.Trim()).ToUpper(CultureInfo.CurrentCulture);
        root.Children.Add(new Border
        {
            Width = 86 * u, Height = 190 * u, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, H * .3, 0, 0), CornerRadius = new CornerRadius(16 * u, 0, 0, 16 * u),
            Background = new LinearGradientBrush(Lighten(accent, .12), Darken(accent, .82), 0),
            Child = Label(initial, DmSerif, 64 * u, FontWeights.Normal, C("#F7F1E6"), TextAlignment.Center),
        });
        ((FrameworkElement)((Border)root.Children[^1]).Child).VerticalAlignment = VerticalAlignment.Center;

        // The entry.
        var entry = new StackPanel { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var headword = FitText(word, DmSerif, FontWeights.Normal, FontStyles.Normal, ink, width, 330 * u, 190 * u, 70 * u, TextAlignment.Left, 1.0);
        entry.Children.Add(headword);
        var category = d.Text("category");
        var cls = d.Text("class");
        var grammar = new TextBlock { Margin = new Thickness(4 * u, 14 * u, 0, 0), TextWrapping = TextWrapping.Wrap };
        grammar.Inlines.Add(new System.Windows.Documents.Run("/" + Syllables(word) + "/   ") { FontFamily = Georgia, FontSize = 30 * u, Foreground = Brush(grey) });
        grammar.Inlines.Add(new System.Windows.Documents.Run(string.IsNullOrWhiteSpace(category) ? "n." : category)
            { FontFamily = Georgia, FontStyle = FontStyles.Italic, FontSize = 34 * u, Foreground = Brush(accent) });
        if (!string.IsNullOrWhiteSpace(cls) || d.Album is not null)
            grammar.Inlines.Add(new System.Windows.Documents.Run("  ·  " + (string.IsNullOrWhiteSpace(cls) ? d.Album : cls))
                { FontFamily = Georgia, FontStyle = FontStyles.Italic, FontSize = 30 * u, Foreground = Brush(grey) });
        entry.Children.Add(grammar);
        entry.Children.Add(new Border { Width = 96 * u, Height = 3 * u, Background = Brush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4 * u, 44 * u, 0, 40 * u) });

        // Definition: hanging number, the lines in a reading serif.
        var definition = new Grid();
        definition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62 * u) });
        definition.ColumnDefinitions.Add(new ColumnDefinition());
        var number = Label(d.Text("number") + ".", Figtree, 34 * u, FontWeights.Bold, accent, TextAlignment.Left);
        number.Margin = new Thickness(0, 8 * u, 0, 0);
        definition.Children.Add(number);
        var face = d.UserFace;
        var reserved = 130 * u + 330 * u + 200 * u + 240 * u + 160 * u;
        var meaning = FitText(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, FontStyles.Normal, ink,
            width - 62 * u, Math.Max(200 * u, H - reserved), (H > 1500 ? 68 : 58) * u * d.TextScale, 24 * u, d_Align(d, TextAlignment.Left), 1.32);
        Grid.SetColumn(meaning, 1);
        definition.Children.Add(meaning);
        entry.Children.Add(definition);

        // Citation and cross-reference.
        var cite = new TextBlock { Margin = new Thickness(62 * u, 34 * u, 0, 0), TextWrapping = TextWrapping.Wrap, FontFamily = Georgia, FontSize = 28 * u, Foreground = Brush(grey) };
        cite.Inlines.Add(new System.Windows.Documents.Run("— "));
        cite.Inlines.Add(new System.Windows.Documents.Run("“" + d.Title + "”") { FontStyle = FontStyles.Italic });
        var note = d.Text("note");
        if (!string.IsNullOrWhiteSpace(note))
            cite.Inlines.Add(new System.Windows.Documents.Run(", " + note));
        entry.Children.Add(cite);
        entry.Children.Add(new TextBlock
        {
            Margin = new Thickness(62 * u, 22 * u, 0, 0), FontFamily = Georgia, FontSize = 24 * u, Foreground = Brush(Alpha(grey, 200)),
            Text = "See also  " + (d.Album ?? d.Artist).ToUpper(CultureInfo.CurrentCulture),
        });
        entry.Margin = new Thickness(left, (d.Wide ? 190 : H > 1500 ? 280 : 200) * u, right, 0);
        root.Children.Add(entry);

        // A real page goes on: the next entries, quieter, below a gap (only where there is room).
        if (!d.Wide && H > 1200)
        {
            var next = new StackPanel { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(left, 0, right, 170 * u), Opacity = .5 };
            TextBlock Neighbour(string word2, string body)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = Georgia, FontSize = 30 * u, Foreground = Brush(ink), Margin = new Thickness(0, 0, 0, 22 * u), LineHeight = 42 * u, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                t.Inlines.Add(new System.Windows.Documents.Run(word2 + " ") { FontFamily = DmSerif, FontSize = 40 * u });
                t.Inlines.Add(new System.Windows.Documents.Run("/" + Syllables(word2) + "/ ") { Foreground = Brush(grey) });
                t.Inlines.Add(new System.Windows.Documents.Run("n. ") { FontStyle = FontStyles.Italic, Foreground = Brush(accent) });
                t.Inlines.Add(new System.Windows.Documents.Run(body));
                return t;
            }
            next.Children.Add(new Border { Height = 1.2 * u, Background = Brush(Alpha(ink, 90)), Margin = new Thickness(0, 0, 0, 30 * u) });
            if (!string.IsNullOrWhiteSpace(d.Artist))
                next.Children.Add(Neighbour(d.Artist, "1. The voice behind \u201C" + d.Title + "\u201D. 2. Someone worth listening to again."));
            if (!string.IsNullOrWhiteSpace(d.Album))
                next.Children.Add(Neighbour(d.Album!, "A record kept on repeat; see " + word.ToLower(CultureInfo.CurrentCulture) + "."));
            root.Children.Add(next);
        }

        // Folio and signature at the foot of the page.
        var foot = new Grid { Margin = new Thickness(left, 0, right, 70 * u), VerticalAlignment = VerticalAlignment.Bottom };
        foot.Children.Add(d.Signature(Alpha(ink, 210), paper, TextAlignment.Left));
        foot.Children.Add(Label(d.Text("number") + "27", Georgia, 24 * u, FontWeights.Normal, grey, TextAlignment.Right));
        root.Children.Add(foot);
        return root;
    }

    /// <summary>A dictionary-style respelling: the word in lower case, its vowel groups marked off.</summary>
    private static string Syllables(string word)
    {
        var lower = word.Trim().ToLower(CultureInfo.CurrentCulture);
        if (lower.Length == 0)
            return "—";
        var parts = new List<string>();
        var current = "";
        var vowel = false;
        foreach (var c in lower)
        {
            var isVowel = "aeiouyáàâãéêíóôõúü".Contains(c);
            if (c == ' ')
            {
                parts.Add(current);
                current = "";
                vowel = false;
                continue;
            }
            if (vowel && !isVowel && current.Length > 1)
            {
                parts.Add(current);
                current = "";
            }
            current += c;
            vowel = isVowel;
        }
        parts.Add(current);
        return string.Join("·", parts.Where(p => p.Length > 0));
    }

    // ======================= Personal letter =======================

    /// <summary>
    /// A letter on ruled paper, folded in three, lying on its envelope: light, human serif text
    /// set on the rules, the date and greeting in italics, and the envelope's stamp and postmark.
    /// </summary>
    private static FrameworkElement PersonalLetter(Studio d)
    {
        const double w = 860, h = 1100, rule = 74;
        var paper = C("#FBF7EE");
        var ink = C("#22304A");
        var canvas = new Canvas { Width = w + 120, Height = h + 120 };

        // The envelope underneath, turned a little.
        var envelope = new Grid { Width = w - 40, Height = 600, Background = new LinearGradientBrush(C("#E2CFAE"), C("#C9B08A"), 90) };
        envelope.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0 L410,330 L820,0"), Stroke = Brush(Alpha(C("#8C7350"), 120)), StrokeThickness = 3,
            Stretch = Stretch.None,
        });
        var stamp = new Border
        {
            Width = 120, Height = 146, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 34, 40, 0), Background = Brush(C("#F8F2E4")), Padding = new Thickness(9),
            BorderBrush = Brush(C("#F8F2E4")), BorderThickness = new Thickness(0),
            Child = new Border
            {
                Background = new LinearGradientBrush(Lighten(d.Accent, .2), Darken(d.Accent, .75), 90),
                Child = Label("♪", Symbols, 54, FontWeights.Normal, C("#F8F2E4"), TextAlignment.Center),
            },
        };
        ((FrameworkElement)((Border)stamp.Child).Child).VerticalAlignment = VerticalAlignment.Center;
        envelope.Children.Add(stamp);
        for (var i = 0; i < 2; i++)
            envelope.Children.Add(new Ellipse
            {
                Width = 150, Height = 150, Stroke = Brush(Alpha(C("#3A3A46"), 110)), StrokeThickness = 3,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 46 + (i * 0), 120 + (i * 22), 0),
            });
        envelope.RenderTransformOrigin = new Point(.5, .5);
        envelope.RenderTransform = new RotateTransform(5);
        envelope.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 14, Direction = 280, Opacity = .35 };
        canvas.Children.Add(At(envelope, 110, h - 470));

        // The sheet: ruled, with a red margin and the two folds.
        var sheet = new Grid { Width = w, Height = h };
        var rules = new DrawingGroup();
        rules.Children.Add(new GeometryDrawing(Brush(paper), null, new RectangleGeometry(new Rect(0, 0, 10, rule))));
        rules.Children.Add(new GeometryDrawing(Brush(C("#C8D5E4")), null, new RectangleGeometry(new Rect(0, rule - 2, 10, 2))));
        sheet.Children.Add(new Border
        {
            Background = new DrawingBrush(rules)
            {
                TileMode = TileMode.Tile, Viewport = new Rect(0, 150 - rule, 10, rule), ViewportUnits = BrushMappingMode.Absolute,
            },
        });
        sheet.Children.Add(new Border { Height = 150, VerticalAlignment = VerticalAlignment.Top, Background = Brush(paper) });
        sheet.Children.Add(new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(112, 0, 0, 0), Background = Brush(C("#E2A3A0")) });
        foreach (var fold in new[] { h / 3, h * 2 / 3 })
        {
            sheet.Children.Add(new Border
            {
                Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, fold - 12, 0, 0),
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, 20), .48), new(Alpha(Colors.White, 90), .54), new(Alpha(Colors.White, 0), 1),
                }, 90),
            });
        }

        // Writing: date, greeting, the lines on the rules, the closing and the name.
        var face = d.UserFace;
        var text = new StackPanel { Margin = new Thickness(146, 54, 74, 0) };
        var date = Label(d.Text("date"), Georgia, 30, FontWeights.Normal, Alpha(ink, 200), TextAlignment.Right);
        date.FontStyle = FontStyles.Italic;
        date.Height = 96;
        text.Children.Add(date);
        var hello = Label(d.Text("hello"), Georgia, 50, FontWeights.Normal, ink, TextAlignment.Left);
        hello.FontStyle = FontStyles.Italic;
        hello.Height = rule * 2;
        hello.VerticalAlignment = VerticalAlignment.Top;
        hello.Padding = new Thickness(0, 4, 0, 0);
        text.Children.Add(hello);
        var body = FitText(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, FontStyles.Normal, ink,
            w - 146 - 74, rule * 7, 56 * d.TextScale, 24, d_Align(d, TextAlignment.Left), 0);
        // On the rules: each line of text is exactly one rule high.
        body.LineHeight = rule;
        body.Padding = new Thickness(0, 6, 0, 0);
        text.Children.Add(body);
        var bye = Label(d.Text("bye"), Georgia, 40, FontWeights.Normal, ink, TextAlignment.Right);
        bye.FontStyle = FontStyles.Italic;
        bye.Margin = new Thickness(0, rule, 0, 0);
        text.Children.Add(bye);
        var name = Label(d.Text("sign"), Georgia, 56, FontWeights.Bold, ink, TextAlignment.Right);
        name.FontStyle = FontStyles.Italic;
        text.Children.Add(name);
        var ps = Label("P.S. — " + d.Title + (string.IsNullOrWhiteSpace(d.Artist) ? "" : ", " + d.Artist), Georgia, 28, FontWeights.Normal, Alpha(ink, 170), TextAlignment.Left);
        ps.FontStyle = FontStyles.Italic;
        ps.Margin = new Thickness(0, rule * .8, 0, 0);
        text.Children.Add(ps);
        sheet.Children.Add(text);
        var paperShape = new Border { Width = w, Height = h, Child = sheet, ClipToBounds = true };
        paperShape.RenderTransformOrigin = new Point(.5, .5);
        paperShape.RenderTransform = new RotateTransform(-2.2);
        paperShape.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 16, Direction = 280, Opacity = .33 };
        canvas.Children.Add(At(paperShape, 40, 20));

        var backdrop = Desk(C("#7B6656"), C("#3A2E27"));
        return ObjectStage(d, canvas, canvas.Width, canvas.Height, backdrop, C("#F3E9DC"), C("#3A2E27"));
    }

    // ======================= Boarding pass =======================

    /// <summary>
    /// A printed boarding pass: the airline band, the route as two big airport codes, the lines
    /// as the passenger's message, the flight fields, the perforation with its notches, and the
    /// stub with a 2D code and a barcode.
    /// </summary>
    private static FrameworkElement BoardingPassTicket(Studio d)
    {
        const double w = 820, h = 1500, tear = 1088;
        var paper = C("#FBFAF6");
        var ink = C("#13203A");
        var grey = C("#7C8392");
        var band = Luminance(d.Accent) > .5 ? Darken(d.Accent, .45) : Darken(d.Accent, .9);
        string Code(string value) => new string(value.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant().PadRight(3, '•');
        var from = d.Text("from");
        var to = d.Text("to");

        // Paper with the two notches at the perforation.
        var outline = new CombinedGeometry(GeometryCombineMode.Exclude,
            RoundRect(0, 0, w, h, 26, 26, 26, 26),
            new GeometryGroup { Children = { new EllipseGeometry(new Point(0, tear), 30, 30), new EllipseGeometry(new Point(w, tear), 30, 30) } });
        var canvas = new Canvas { Width = w, Height = h, Clip = outline };
        canvas.Children.Add(new Path { Data = outline, Fill = Fibers(paper, Alpha(C("#C8C2B4"), 22)) });
        canvas.Children.Add(At(Label("✈", Symbols, 520, FontWeights.Normal, Alpha(ink, 9), TextAlignment.Left), 150, 380));

        // Airline band.
        canvas.Children.Add(At(new Border { Width = w, Height = 150, Background = new LinearGradientBrush(Lighten(band, .1), band, 0) }, 0, 0));
        canvas.Children.Add(At(Spaced("TASKITIFY AIRLINES", Figtree, 32, FontWeights.ExtraBold, C("#FFFFFF"), TextAlignment.Left, .2), 48, 38));
        canvas.Children.Add(At(Spaced("BOARDING PASS · " + d.Text("id"), Figtree, 18, FontWeights.SemiBold, Alpha(Colors.White, 200), TextAlignment.Left, .2), 50, 88));
        canvas.Children.Add(At(Label("✈", Symbols, 58, FontWeights.Normal, Colors.White, TextAlignment.Left), 710, 36));

        // Route.
        void Airport(string code, string city, double x, TextAlignment align)
        {
            var stack = new StackPanel { Width = 330 };
            var big = Label(code, Figtree, 132, FontWeights.Black, ink, align);
            big.TextTrimming = TextTrimming.None;
            stack.Children.Add(big);
            var name = Spaced(city.ToUpperInvariant(), Figtree, 20, FontWeights.Bold, grey, align, .2);
            name.Margin = new Thickness(0, -14, 0, 0);
            stack.Children.Add(name);
            canvas.Children.Add(At(stack, x, 176));
        }
        Airport(Code(from), from, 46, TextAlignment.Left);
        Airport(Code(to), to, w - 376, TextAlignment.Right);
        canvas.Children.Add(At(new Line { X1 = 0, X2 = 120, Stroke = Brush(Alpha(ink, 120)), StrokeThickness = 2.5, StrokeDashArray = [3, 3] }, 350, 268));
        canvas.Children.Add(At(Label("✈", Symbols, 40, FontWeights.Normal, band, TextAlignment.Left), 388, 238));
        canvas.Children.Add(At(Label(d.Duration, Figtree, 18, FontWeights.SemiBold, grey, TextAlignment.Center), 372, 292));

        // Message: the lines.
        canvas.Children.Add(At(Spaced("PASSENGER MESSAGE", Figtree, 17, FontWeights.Bold, grey, TextAlignment.Left, .3), 48, 400));
        var face = d.UserFace;
        var lyric = FitText(d.LyricText, face?.Family ?? InterDisplay, face?.Weight ?? FontWeights.SemiBold, FontStyles.Normal, ink,
            w - 96, 320, 62 * d.TextScale, 22, d_Align(d, TextAlignment.Left), 1.14);
        canvas.Children.Add(At(lyric, 48, 436));

        // Flight fields.
        var fields = new UniformGrid2(3, 2) { Width = w - 96 };
        foreach (var (label, value) in new[]
        {
            ("PASSENGER", d.Artist), ("FLIGHT", d.Text("id")), ("DATE", d.Text("date")),
            ("GATE", "A" + d.Time.Replace(":", "")), ("SEAT", "27A"), ("BOARDING", d.Time),
        })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 26) };
            cell.Children.Add(Spaced(label, Figtree, 15, FontWeights.Bold, grey, TextAlignment.Left, .3));
            var v = Label(value.ToUpper(CultureInfo.CurrentCulture), Figtree, 26, FontWeights.Bold, ink, TextAlignment.Left);
            v.TextWrapping = TextWrapping.Wrap;
            v.TextTrimming = TextTrimming.None;
            v.Margin = new Thickness(0, 4, 0, 0);
            cell.Children.Add(v);
            fields.Add(cell);
        }
        canvas.Children.Add(At(new Border { Width = w - 96, Height = 1.5, Background = Brush(Alpha(ink, 60)) }, 48, 770));
        canvas.Children.Add(At(fields.Build(), 48, 800));
        canvas.Children.Add(At(Label(d.Text("note"), Georgia, 22, FontWeights.Normal, grey, TextAlignment.Left), 48, 1000));
        ((TextBlock)canvas.Children[^1]).FontStyle = FontStyles.Italic;

        // Perforation.
        canvas.Children.Add(At(new Line { X1 = 0, X2 = w - 80, Stroke = Brush(Alpha(ink, 90)), StrokeThickness = 3, StrokeDashArray = [1.2, 2.4] }, 40, tear));

        // Stub: route again, seat, a 2D code and a barcode.
        canvas.Children.Add(At(Label(Code(from) + "  ✈  " + Code(to), Figtree, 46, FontWeights.Black, ink, TextAlignment.Left), 48, tear + 50));
        canvas.Children.Add(At(Spaced("SEAT 27A  ·  GROUP 1  ·  " + d.Text("id"), Figtree, 17, FontWeights.Bold, grey, TextAlignment.Left, .25), 50, tear + 118));
        canvas.Children.Add(At(Label(d.Title, Figtree, 24, FontWeights.SemiBold, ink, TextAlignment.Left), 50, tear + 158));
        ((TextBlock)canvas.Children[^1]).Width = 440;
        canvas.Children.Add(At(Aztec(ink, 200), w - 248, tear + 44));
        canvas.Children.Add(At(LinearBarcode(ink, w - 96, 78), 48, tear + 262));
        canvas.Children.Add(At(Label("0027 4815 1623 4200 " + d.Text("id").Replace("–", "").Replace("-", ""), Mono, 18, FontWeights.SemiBold, grey, TextAlignment.Left), 48, tear + 350));

        var ticket = new Grid { Width = w, Height = h, Children = { canvas } };
        ticket.RenderTransformOrigin = new Point(.5, .5);
        ticket.RenderTransform = new RotateTransform(-2.5);
        ticket.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 22, Direction = 280, Opacity = .4 };
        var holder = new Canvas { Width = w + 80, Height = h + 80 };
        holder.Children.Add(At(ticket, 40, 40));
        var backdrop = Desk(Lighten(Darken(d.Accent, .6), .12), Darken(d.Accent, .28));
        return ObjectStage(d, holder, holder.Width, holder.Height, backdrop, Colors.White, Darken(d.Accent, .3));
    }

    /// <summary>A square 2D code (deterministic modules with the three finder squares).</summary>
    private static FrameworkElement Aztec(Color ink, double size)
    {
        const int n = 25;
        var cell = size / n;
        var canvas = new Canvas { Width = size, Height = size };
        var random = new Random(27);
        var modules = new GeometryGroup();
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var finder = (x < 7 && y < 7) || (x >= n - 7 && y < 7) || (x < 7 && y >= n - 7);
                if (!finder && random.NextDouble() < .5)
                    modules.Children.Add(new RectangleGeometry(new Rect(x * cell, y * cell, cell + .3, cell + .3)));
            }
        foreach (var (fx, fy) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            modules.Children.Add(new RectangleGeometry(new Rect(fx * cell, fy * cell, 7 * cell, cell)));
            modules.Children.Add(new RectangleGeometry(new Rect(fx * cell, (fy + 6) * cell, 7 * cell, cell)));
            modules.Children.Add(new RectangleGeometry(new Rect(fx * cell, fy * cell, cell, 7 * cell)));
            modules.Children.Add(new RectangleGeometry(new Rect((fx + 6) * cell, fy * cell, cell, 7 * cell)));
            modules.Children.Add(new RectangleGeometry(new Rect((fx + 2) * cell, (fy + 2) * cell, 3 * cell, 3 * cell)));
        }
        canvas.Children.Add(new Path { Data = modules, Fill = Brush(ink) });
        return canvas;
    }

    private static FrameworkElement LinearBarcode(Color ink, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height };
        var random = new Random(48);
        var bars = new GeometryGroup();
        for (var x = 0.0; x < width;)
        {
            var bar = 2 + (random.Next(4) * 1.6);
            if (random.NextDouble() < .55)
                bars.Children.Add(new RectangleGeometry(new Rect(x, 0, bar, height)));
            x += bar + 1.6;
        }
        canvas.Children.Add(new Path { Data = bars, Fill = Brush(ink) });
        return canvas;
    }

    // ======================= Shared pieces =======================

    /// <summary>
    /// An object drawn at its own size, scaled as a whole to fit the card (above the signature),
    /// on a backdrop: proportions never change with the format.
    /// </summary>
    private static FrameworkElement ObjectStage(Studio d, FrameworkElement item, double width, double height, Brush backdrop, Color fg, Color bg)
    {
        var u = d.U;
        var margin = (d.Wide ? 70 : 90) * u;
        var room = new Size(d.CardWidth - (2 * margin), d.Height - (2 * margin) - (90 * u));
        var scale = Math.Min(room.Width / width, room.Height / height);
        var root = new Grid { Background = backdrop };
        var box = new Viewbox { Width = width * scale, Height = height * scale, Child = item, Stretch = Stretch.Uniform };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(box);
        var signature = d.Signature(fg, bg);
        signature.Margin = new Thickness(0, 46 * u, 0, 0);
        stack.Children.Add(signature);
        root.Children.Add(stack);
        return root;
    }

    /// <summary>The text at the largest size (down to <paramref name="min"/>) whose wrapped height fits.</summary>
    private static TextBlock FitText(string text, FontFamily font, FontWeight weight, FontStyle style, Color ink, double width,
        double maxHeight, double start, double min, TextAlignment align, double lineFactor)
    {
        for (var size = start; ; size *= .94)
        {
            var block = new TextBlock
            {
                Text = text, FontFamily = font, FontWeight = weight, FontStyle = style, FontSize = size, Foreground = Brush(ink),
                TextWrapping = TextWrapping.Wrap, TextAlignment = align, Width = width,
            };
            if (lineFactor > 0)
            {
                block.LineHeight = size * lineFactor;
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            block.Measure(new Size(width, double.PositiveInfinity));
            if (block.DesiredSize.Height <= maxHeight || size * .94 < min)
                return block;
        }
    }

    private static TextAlignment d_Align(Studio d, TextAlignment fallback) => d.Align(fallback);

    private static T At<T>(T element, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }

    /// <summary>A rectangle with each corner's own radius.</summary>
    private static Geometry RoundRect(double x, double y, double w, double h, double tl, double tr, double br, double bl)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x + tl, y), true, true);
            c.LineTo(new Point(x + w - tr, y), true, false);
            c.ArcTo(new Point(x + w, y + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x + w, y + h - br), true, false);
            c.ArcTo(new Point(x + w - br, y + h), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x + bl, y + h), true, false);
            c.ArcTo(new Point(x, y + h - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x, y + tl), true, false);
            c.ArcTo(new Point(x + tl, y), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A rows × columns grid of cells, filled in reading order.</summary>
    private sealed class UniformGrid2(int columns, int rows)
    {
        private readonly List<UIElement> _cells = [];

        public double Width { get; init; }

        public void Add(UIElement cell) => _cells.Add(cell);

        public FrameworkElement Build()
        {
            var grid = new Grid { Width = Width };
            for (var c = 0; c < columns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < _cells.Count; i++)
            {
                Grid.SetColumn(_cells[i], i % columns);
                Grid.SetRow(_cells[i], i / columns);
                grid.Children.Add(_cells[i]);
            }
            return grid;
        }
    }
}
