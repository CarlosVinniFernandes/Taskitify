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
    /// A page torn from a reference book, kept: running head and guide word, the headword alone
    /// and large, its respelling and grammar, the lines as the numbered definition, the citation,
    /// the next entries in two tight columns. Someone read it with a pencil: the headword is
    /// underlined, a heart and a date sit in the margin. A ribbon marks the page; the spine
    /// shades its inner edge; a thumb index is cut into the outer one.
    /// </summary>
    private static FrameworkElement DictionaryEntry(Studio d)
    {
        var u = d.U;
        var W = d.CardWidth;
        var H = d.Height;
        var paper = C("#F3EEE2");
        var ink = C("#1D1B18");
        var grey = C("#766D62");
        var pencil = C("#5E5B57");
        var accent = Luminance(d.Accent) > .55 ? Darken(d.Accent, .5) : d.Accent;
        var word = d.Text("word");
        var root = new Grid { Background = Fibers(paper, Alpha(C("#B5A487"), 40)) };
        // The spine's shade on the inner edge, and a little age at the outer one.
        root.Children.Add(new Border { Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Alpha(C("#3B2E1E"), 70), 0), new(Alpha(C("#3B2E1E"), 0), .09), new(Alpha(C("#3B2E1E"), 0), .93), new(Alpha(C("#8A6A3E"), 34), 1),
        }, 0) });
        var left = (d.Wide ? 200 : 128) * u;
        var right = (d.Wide ? 240 : 158) * u;
        var width = W - left - right;
        var tall = !d.Wide && H > 1500;

        // Running head: small capitals, rule, guide word in italics, folio.
        var head = new Grid { Margin = new Thickness(left, 86 * u, right, 0), VerticalAlignment = VerticalAlignment.Top };
        head.Children.Add(Spaced("DICTIONARY OF SONGS", Figtree, 18 * u, FontWeights.Bold, grey, TextAlignment.Left, .32));
        var guide = Label(word.ToLower(CultureInfo.CurrentCulture) + "  —  " + d.Text("number") + "27", Georgia, 23 * u, FontWeights.Normal, grey, TextAlignment.Right);
        guide.FontStyle = FontStyles.Italic;
        head.Children.Add(guide);
        root.Children.Add(head);
        root.Children.Add(new Border { Height = 1.4 * u, Background = Brush(Alpha(ink, 140)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, 126 * u, right, 0) });
        root.Children.Add(new Border { Height = .8 * u, Background = Brush(Alpha(ink, 90)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, 131 * u, right, 0) });

        // Thumb index and ribbon.
        var initial = string.IsNullOrWhiteSpace(word) ? "A" : StringInfo.GetNextTextElement(word.Trim()).ToUpper(CultureInfo.CurrentCulture);
        var tab = Label(initial, DmSerif, 60 * u, FontWeights.Normal, C("#F7F1E6"), TextAlignment.Center);
        tab.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(new Border
        {
            Width = 84 * u, Height = 184 * u, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, H * .32, 0, 0), CornerRadius = new CornerRadius(14 * u, 0, 0, 14 * u),
            Background = new LinearGradientBrush(Lighten(accent, .1), Darken(accent, .8), 0), Child = tab,
        });
        root.Children.Add(Ribbon(accent, 40 * u, (tall ? 430 : 300) * u, HorizontalAlignment.Right, new Thickness(0, 0, 96 * u, 0)));

        // The entry.
        var entry = new StackPanel { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var headword = FitText(word, DmSerif, FontWeights.Normal, FontStyles.Normal, ink, width - 40 * u, 330 * u, 186 * u, 70 * u, TextAlignment.Left, 1.0);
        var wordBox = new Grid();
        wordBox.Children.Add(headword);
        // A pencil underline, drawn quickly under the headword.
        headword.Measure(new Size(width, double.PositiveInfinity));
        var under = headword.DesiredSize.Height + (6 * u);
        var lastLine = word.Contains(' ') && MeasureWidth(word, DmSerif, headword.FontSize) > width - 40 * u ? word[(word.LastIndexOf(' ') + 1)..] : word;
        var span = Math.Min(width * .7, MeasureWidth(lastLine, DmSerif, headword.FontSize) * 1.05);
        wordBox.Children.Add(Pencil(Geometry.Parse(FormattableString.Invariant(
            $"M{4 * u},{under} C{span * .3},{under + (9 * u)} {span * .62},{under - (5 * u)} {span},{under + (4 * u)}")), pencil, 3.2 * u));
        entry.Children.Add(wordBox);

        var category = d.Text("category");
        var cls = d.Text("class");
        var grammar = new TextBlock { Margin = new Thickness(4 * u, 22 * u, 0, 0), TextWrapping = TextWrapping.Wrap, FontFamily = Georgia, FontSize = 30 * u, Foreground = Brush(grey) };
        grammar.Inlines.Add(new System.Windows.Documents.Run("/" + Syllables(word) + "/   "));
        grammar.Inlines.Add(new System.Windows.Documents.Run(string.IsNullOrWhiteSpace(category) ? "n." : category)
            { FontStyle = FontStyles.Italic, FontSize = 34 * u, Foreground = Brush(accent) });
        var context = string.IsNullOrWhiteSpace(cls) ? d.Album : cls;
        if (!string.IsNullOrWhiteSpace(context))
        {
            grammar.Inlines.Add(new System.Windows.Documents.Run("   ["));
            grammar.Inlines.Add(new System.Windows.Documents.Run(context.ToUpper(CultureInfo.CurrentCulture)) { FontSize = 22 * u, FontFamily = Figtree, FontWeight = FontWeights.Bold });
            grammar.Inlines.Add(new System.Windows.Documents.Run("]"));
        }
        entry.Children.Add(grammar);
        entry.Children.Add(new Border { Width = 96 * u, Height = 3 * u, Background = Brush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4 * u, 40 * u, 0, 38 * u) });

        // Definition: hanging bold numeral, the lines in a reading serif.
        var definition = new Grid();
        definition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60 * u) });
        definition.ColumnDefinitions.Add(new ColumnDefinition());
        var number = Label(d.Text("number") + ".", Georgia, 40 * u, FontWeights.Bold, accent, TextAlignment.Left);
        definition.Children.Add(number);
        var face = d.UserFace;
        var meaning = FitText(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, FontStyles.Normal, ink,
            width - 60 * u, (tall ? 440 : 260) * u, (tall ? 66 : 54) * u * d.TextScale, 24 * u, d.Align(TextAlignment.Left), 1.3);
        Grid.SetColumn(meaning, 1);
        definition.Children.Add(meaning);
        entry.Children.Add(definition);

        var cite = new TextBlock { Margin = new Thickness(60 * u, 30 * u, 0, 0), TextWrapping = TextWrapping.Wrap, FontFamily = Georgia, FontSize = 27 * u, Foreground = Brush(grey) };
        cite.Inlines.Add(new System.Windows.Documents.Run("— "));
        cite.Inlines.Add(new System.Windows.Documents.Run("“" + d.Title + "”") { FontStyle = FontStyles.Italic });
        var note = d.Text("note");
        if (!string.IsNullOrWhiteSpace(note))
            cite.Inlines.Add(new System.Windows.Documents.Run(", " + note + (d.Album is { } album ? " (" + album + ")" : "")));
        entry.Children.Add(cite);
        entry.Margin = new Thickness(left, (d.Wide ? 186 : tall ? 262 : 186) * u, right, 0);
        root.Children.Add(entry);

        // The reader's pencil in the margin: a heart and the day.
        var margin = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Max(10 * u, left - 120 * u), (d.Wide ? 470 : tall ? 640 : 470) * u, 0, 0),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-9),
        };
        margin.Children.Add(Label("♡", Symbols, 44 * u, FontWeights.Normal, pencil, TextAlignment.Center));
        margin.Children.Add(Label(DateTime.Today.ToString("dd.MM", CultureInfo.InvariantCulture), HandFont, 34 * u, FontWeights.Normal, pencil, TextAlignment.Center));
        root.Children.Add(margin);

        // The page goes on: the next entries in two tight columns with a rule between them.
        if (!d.Wide && H > 1200)
        {
            var columns = new Grid { Width = width, Margin = new Thickness(0, 120 * u, 0, 0), Opacity = .62 };
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44 * u) });
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock Neighbour(string word2, string body)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = Georgia, FontSize = 24 * u, Foreground = Brush(ink),
                    LineHeight = 33 * u, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = TextAlignment.Left };
                t.Inlines.Add(new System.Windows.Documents.Run(word2 + " ") { FontWeight = FontWeights.Bold, FontSize = 27 * u });
                t.Inlines.Add(new System.Windows.Documents.Run("/" + Syllables(word2) + "/ ") { Foreground = Brush(grey) });
                t.Inlines.Add(new System.Windows.Documents.Run("n. ") { FontStyle = FontStyles.Italic, Foreground = Brush(accent) });
                t.Inlines.Add(new System.Windows.Documents.Run(body));
                return t;
            }
            columns.Children.Add(new Border { Height = 1.2 * u, Background = Brush(Alpha(ink, 80)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -30 * u, 0, 0) });
            Grid.SetColumnSpan(columns.Children[^1], 3);
            var first = Neighbour(string.IsNullOrWhiteSpace(d.Artist) ? "song" : d.Artist,
                "1. The voice behind “" + d.Title + "”. 2. Someone worth listening to again, often late at night.");
            columns.Children.Add(first);
            var second = Neighbour(string.IsNullOrWhiteSpace(d.Album) ? "refrain" : d.Album!,
                "A record kept on repeat. — See " + word.ToLower(CultureInfo.CurrentCulture) + ".");
            var lastWord = word.Trim().Split(' ').Last();
            var stackRight = new StackPanel();
            stackRight.Children.Add(second);
            var third = Neighbour(lastWord.ToLower(CultureInfo.CurrentCulture), "1. What the song is about. 2. Fig. Anything played too loud, too often, with the windows down.");
            third.Margin = new Thickness(0, 18 * u, 0, 0);
            stackRight.Children.Add(third);
            Grid.SetColumn(stackRight, 2);
            columns.Children.Add(stackRight);
            var rule = new Border { Width = 1, Background = Brush(Alpha(ink, 70)), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(rule, 1);
            columns.Children.Add(rule);
            entry.Children.Add(columns);
        }

        var foot = new Grid { Margin = new Thickness(left, 0, right, 70 * u), VerticalAlignment = VerticalAlignment.Bottom };
        foot.Children.Add(d.Signature(Alpha(ink, 200), paper, TextAlignment.Left));
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
    /// A letter written by hand: fountain-pen ink on ruled paper, each line a little off the
    /// rule and a little tilted the way a hand moves, the name signed with a flourish, a note
    /// squeezed into the margin. The sheet is folded in three, taped at a corner, and lies on its
    /// air-mail envelope with stamp and postmark; a cup once stood on it.
    /// </summary>
    private static FrameworkElement PersonalLetter(Studio d)
    {
        const double w = 860, h = 1120, rule = 72, top = 214, inner = 150;
        var paper = C("#FBF6EA");
        var ink = C("#1E3772");
        var canvas = new Canvas { Width = w + 140, Height = h + 170 };

        // Air-mail envelope underneath, its striped border showing.
        var envelope = new Grid { Width = w - 30, Height = 560, Background = new LinearGradientBrush(C("#F1EADB"), C("#E3D8C2"), 90) };
        envelope.Children.Add(new Border { BorderThickness = new Thickness(18), BorderBrush = AirMail() });
        envelope.Children.Add(new Path { Data = Geometry.Parse("M18,18 L415,300 L812,18"), Stroke = Brush(Alpha(C("#8C7350"), 110)), StrokeThickness = 2.5 });
        var stamp = new Border
        {
            Width = 118, Height = 142, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 58, 46), Background = Brush(C("#FBF7EC")), Padding = new Thickness(9),
            Child = new Border
            {
                Background = new LinearGradientBrush(Lighten(d.Accent, .25), Darken(d.Accent, .7), 90),
                Child = Label("♪", Symbols, 52, FontWeights.Normal, C("#FBF7EC"), TextAlignment.Center),
            },
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(3),
        };
        ((FrameworkElement)((Border)stamp.Child).Child).VerticalAlignment = VerticalAlignment.Center;
        envelope.Children.Add(stamp);
        // Postmark: a ring with the day, and the wavy cancellation lines across the stamp.
        var postmark = new Grid { Width = 140, Height = 140, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 130, 70), Opacity = .62 };
        postmark.Children.Add(new Ellipse { Stroke = Brush(C("#2D2C3A")), StrokeThickness = 3 });
        postmark.Children.Add(Label(DateTime.Today.ToString("dd MMM\nyyyy", CultureInfo.InvariantCulture).ToUpperInvariant(), Courier, 18, FontWeights.Bold, C("#2D2C3A"), TextAlignment.Center));
        ((FrameworkElement)postmark.Children[^1]).VerticalAlignment = VerticalAlignment.Center;
        envelope.Children.Add(postmark);
        envelope.Children.Add(new Path
        {
            Data = Geometry.Parse("M560,430 c20,-14 40,14 60,0 s40,14 60,0 s40,14 60,0 s40,14 60,0 M560,452 c20,-14 40,14 60,0 s40,14 60,0 s40,14 60,0 s40,14 60,0 M560,474 c20,-14 40,14 60,0 s40,14 60,0 s40,14 60,0 s40,14 60,0"),
            Stroke = Brush(Alpha(C("#2D2C3A"), 150)), StrokeThickness = 3,
        });
        envelope.RenderTransformOrigin = new Point(.5, .5);
        envelope.RenderTransform = new RotateTransform(4.5);
        envelope.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 14, Direction = 280, Opacity = .35 };
        canvas.Children.Add(At(envelope, 104, h - 370));

        // The sheet: ruled, a red margin, aged edges, two folds, a faint ring from a cup.
        var sheet = new Grid { Width = w, Height = h, Background = Fibers(paper, Alpha(C("#C9B996"), 30)) };
        var rules = new DrawingGroup();
        rules.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 10, rule))));
        rules.Children.Add(new GeometryDrawing(Brush(Alpha(C("#7E9DC4"), 120)), null, new RectangleGeometry(new Rect(0, rule - 1.6, 10, 1.6))));
        sheet.Children.Add(new Border
        {
            Margin = new Thickness(0, top - rule, 0, 40),
            Background = new DrawingBrush(rules) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, rule), ViewportUnits = BrushMappingMode.Absolute },
        });
        sheet.Children.Add(new Border { Width = 2.4, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(inner - 32, 0, 0, 0), Background = Brush(Alpha(C("#D9706B"), 150)) });
        sheet.Children.Add(new Border { BorderThickness = new Thickness(14), BorderBrush = new SolidColorBrush(Alpha(C("#C8A86E"), 26)), Opacity = .9 });
        foreach (var fold in new[] { h / 3, h * 2 / 3 })
            sheet.Children.Add(new Border
            {
                Height = 26, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, fold - 13, 0, 0),
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, 24), .48), new(Alpha(Colors.White, 110), .55), new(Alpha(Colors.White, 0), 1),
                }, 90),
            });
        sheet.Children.Add(new Ellipse
        {
            Width = 250, Height = 236, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -60, 120), StrokeThickness = 9,
            Stroke = new LinearGradientBrush(Alpha(C("#8B5A2B"), 34), Alpha(C("#8B5A2B"), 6), 35),
        });

        // Writing, by hand.
        // Shifted so the writing's baselines rest on the rules.
        var writing = new Canvas { RenderTransform = new TranslateTransform(0, 12) };
        var date = Handwriting(d.Text("date"), HandFont, 40, ink, 330, 11, TextAlignment.Right, rule);
        writing.Children.Add(At(date, w - 330 - 70, top - (rule * 2) + 8));
        var hello = Handwriting(d.Text("hello"), HandFont, 62, ink, w - inner - 80, 12, TextAlignment.Left, rule);
        writing.Children.Add(At(hello, inner - 6, top - rule + 6));
        var face = d.UserFace;
        var body = Handwriting(d.LyricText, face?.Family ?? HandFont, 60 * d.TextScale, ink, w - inner - 70, 13, d.Align(TextAlignment.Left), rule,
            maxHeight: rule * 7, weight: face?.Weight ?? FontWeights.Normal);
        writing.Children.Add(At(body, inner, top + 6));
        body.Measure(new Size(w - inner - 70, double.PositiveInfinity));
        var lines = Math.Max(1, (int)Math.Round(body.DesiredSize.Height / rule));
        var bye = Handwriting(d.Text("bye"), HandFont, 54, ink, 400, 14, TextAlignment.Left, rule);
        writing.Children.Add(At(bye, w - 470, top + ((lines + 1) * rule) + 6));
        var name = Handwriting(d.Text("sign"), HandFont, 84, ink, 440, 15, TextAlignment.Left, rule, weight: FontWeights.Bold);
        writing.Children.Add(At(name, w - 450, top + ((lines + 2) * rule) - 4));
        // The flourish under the name.
        var y = top + ((lines + 3) * rule) + 4;
        writing.Children.Add(Pen(Geometry.Parse(FormattableString.Invariant($"M{w - 440},{y} C{w - 360},{y + 22} {w - 230},{y - 26} {w - 120},{y - 4} S{w - 90},{y + 10} {w - 70},{y - 14}")), ink, 3.4));
        // A note squeezed into the margin, with its arrow.
        var note = Handwriting("♪ " + d.Title, HandFont, 34, Alpha(ink, 225), 330, 16, TextAlignment.Left, 38, maxHeight: 40);
        note.RenderTransformOrigin = new Point(0, 0);
        note.RenderTransform = new RotateTransform(-7);
        var noteY = top + ((lines + 4) * rule) + 20;
        writing.Children.Add(At(note, inner - 10, noteY));
        writing.Children.Add(Pen(Geometry.Parse(FormattableString.Invariant($"M{inner + 40},{noteY - 10} C{inner + 30},{noteY - 60} {inner + 50},{noteY - 110} {inner + 90},{noteY - 140} M{inner + 90},{noteY - 140} l-24,6 M{inner + 90},{noteY - 140} l-4,24")), Alpha(ink, 210), 2.6));
        sheet.Children.Add(writing);

        var sheetBox = new Border { Width = w, Height = h, Child = sheet, ClipToBounds = true };
        sheetBox.RenderTransformOrigin = new Point(.5, .5);
        sheetBox.RenderTransform = new RotateTransform(-2.4);
        sheetBox.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 16, Direction = 280, Opacity = .33 };
        canvas.Children.Add(At(sheetBox, 50, 46));
        // Tape holding the corner.
        canvas.Children.Add(At(Tape(Lighten(d.Accent, .55), 210, 64, -32), w - 120, 16));

        return ObjectStage(d, canvas, canvas.Width, canvas.Height, Desk(C("#7D6858"), C("#382C25")), C("#F3E9DC"), C("#382C25"));
    }

    /// <summary>The red and blue diagonal stripes of an air-mail envelope's border.</summary>
    private static Brush AirMail()
    {
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(Brush(C("#F1EADB")), null, new RectangleGeometry(new Rect(0, 0, 60, 60))));
        tile.Children.Add(new GeometryDrawing(Brush(C("#C8323B")), null, Geometry.Parse("M0,0 L15,0 L60,45 L60,60 L45,60 L0,15 Z")));
        tile.Children.Add(new GeometryDrawing(Brush(C("#24418C")), null, Geometry.Parse("M30,0 L45,0 L60,15 L60,30 Z M0,30 L30,60 L15,60 L0,45 Z")));
        var brush = new DrawingBrush(tile) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 40, 40), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    // ======================= Boarding pass =======================

    /// <summary>
    /// A printed boarding pass in two layers, like a real one: the card stock came preprinted
    /// (airline band, small labels, rules) and the kiosk printed the passenger's data on top in
    /// a thermal monospace, slightly off register and a little faded. Then it was used: the gate
    /// was circled in pen with a note, a "BOARDED" stamp went across, it was folded once, and
    /// the stub was torn along the perforation.
    /// </summary>
    private static FrameworkElement BoardingPassTicket(Studio d)
    {
        const double w = 820, h = 1500, tear = 1088;
        var paper = C("#FAF8F2");
        var printed = C("#1B2236");
        var data = C("#232326");
        var grey = C("#80869A");
        var pen = C("#2546A8");
        var band = Luminance(d.Accent) > .5 ? Darken(d.Accent, .45) : Darken(d.Accent, .9);
        string Code(string value) => new string(value.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant().PadRight(3, '•');
        var from = d.Text("from");
        var to = d.Text("to");

        var outline = new CombinedGeometry(GeometryCombineMode.Exclude, RoundRect(0, 0, w, h, 24, 24, 24, 24),
            new GeometryGroup { Children = { new EllipseGeometry(new Point(0, tear), 28, 28), new EllipseGeometry(new Point(w, tear), 28, 28) } });
        var canvas = new Canvas { Width = w, Height = h, Clip = outline };
        canvas.Children.Add(new Path { Data = outline, Fill = Fibers(paper, Alpha(C("#C4BCA8"), 26)) });
        // Preprinted security pattern: fine guilloche-like lines behind the data.
        canvas.Children.Add(At(Guilloche(Alpha(band, 26), w, 620), 0, 160));
        canvas.Children.Add(At(Label("✈", Symbols, 480, FontWeights.Normal, Alpha(band, 12), TextAlignment.Left), 190, 400));

        // Preprinted band.
        canvas.Children.Add(At(new Border { Width = w, Height = 150, Background = new LinearGradientBrush(Lighten(band, .12), band, 0) }, 0, 0));
        canvas.Children.Add(At(Spaced("TASKITIFY AIRLINES", Figtree, 31, FontWeights.ExtraBold, Colors.White, TextAlignment.Left, .22), 48, 40));
        canvas.Children.Add(At(Spaced("BOARDING PASS  ·  CARTÃO DE EMBARQUE", Figtree, 15, FontWeights.SemiBold, Alpha(Colors.White, 210), TextAlignment.Left, .22), 50, 90));
        canvas.Children.Add(At(Label("✈", Symbols, 56, FontWeights.Normal, Colors.White, TextAlignment.Left), 712, 38));

        // Small preprinted labels; the data printed onto them by the kiosk (thermal, a bit off).
        TextBlock Lbl(string text) => Spaced(text, Figtree, 14, FontWeights.Bold, grey, TextAlignment.Left, .3);
        FrameworkElement Data(string text, double size, int seed, bool bold = true) => Thermal(text, size, data, seed, bold);
        canvas.Children.Add(At(Lbl("FROM / DE"), 48, 180));
        canvas.Children.Add(At(Lbl("TO / PARA"), w - 300, 180));
        canvas.Children.Add(At(Data(Code(from), 120, 1), 40, 196));
        canvas.Children.Add(At(Data(Code(to), 120, 2), w - 306, 196));
        canvas.Children.Add(At(Data(from.ToUpperInvariant(), 20, 3, bold: false), 48, 336));
        canvas.Children.Add(At(Data(to.ToUpperInvariant(), 20, 4, bold: false), w - 300, 336));
        canvas.Children.Add(At(new Line { X1 = 0, X2 = 116, Stroke = Brush(Alpha(printed, 110)), StrokeThickness = 2.5, StrokeDashArray = [3, 3] }, 340, 276));
        canvas.Children.Add(At(Label("✈", Symbols, 38, FontWeights.Normal, band, TextAlignment.Left), 378, 250));

        // The passenger's message: the lines.
        canvas.Children.Add(At(Lbl("PASSENGER MESSAGE / MENSAGEM"), 48, 410));
        var face = d.UserFace;
        var message = face is null
            ? ThermalBlock(d.LyricText, data, w - 96, 300, 52 * d.TextScale, d.Align(TextAlignment.Left))
            : FitText(d.LyricText, face.Value.Family, face.Value.Weight, FontStyles.Normal, data, w - 96, 300, 52 * d.TextScale, 22, d.Align(TextAlignment.Left), 1.16);
        canvas.Children.Add(At(message, 46, 444));

        // Flight fields: label above, printed value below.
        canvas.Children.Add(At(new Border { Width = w - 96, Height = 1.4, Background = Brush(Alpha(printed, 70)) }, 48, 770));
        var fields = new[]
        {
            ("PASSENGER", d.Artist.ToUpperInvariant()), ("FLIGHT", d.Text("id")), ("DATE", d.Text("date").ToUpperInvariant()),
            ("GATE", "A" + d.Time.Replace(":", "")), ("SEAT", "27A"), ("BOARDING", d.Time),
        };
        for (var i = 0; i < fields.Length; i++)
        {
            var x = 48 + ((i % 3) * 248);
            var yy = 796 + ((i / 3) * 100);
            canvas.Children.Add(At(Lbl(fields[i].Item1), x, yy));
            var value = Thermal(fields[i].Item2, 28, data, 10 + i, true);
            ((FrameworkElement)value).Width = 236;
            canvas.Children.Add(At(value, x - 2, yy + 22));
        }
        // The gate circled in pen, with a note.
        canvas.Children.Add(Pen(Geometry.Parse("M32,940 C26,912 148,904 158,932 C166,958 62,968 38,950 C30,942 34,930 48,922"), pen, 3.2));
        var gateNote = Label("gate changed!", HandFont, 38, FontWeights.Normal, pen, TextAlignment.Left);
        gateNote.RenderTransformOrigin = new Point(0, .5);
        gateNote.RenderTransform = new RotateTransform(-6);
        canvas.Children.Add(At(gateNote, 150, 958));
        canvas.Children.Add(At(Label(d.Text("note"), Georgia, 22, FontWeights.Normal, grey, TextAlignment.Left), 48, 1014));
        ((TextBlock)canvas.Children[^1]).FontStyle = FontStyles.Italic;

        // "BOARDED", stamped across the fields.
        canvas.Children.Add(At(RubberStamp("BOARDED", "✈  " + d.Text("date").ToUpperInvariant(), C("#B3262E"), -11, 3), 488, 948));

        // Perforation and the stub.
        canvas.Children.Add(At(new Line { X1 = 0, X2 = w - 80, Stroke = Brush(Alpha(printed, 90)), StrokeThickness = 3, StrokeDashArray = [1.2, 2.4] }, 40, tear));
        canvas.Children.Add(At(Lbl("STUB / CANHOTO"), 50, tear + 34));
        canvas.Children.Add(At(Data(Code(from) + " > " + Code(to), 44, 20), 46, tear + 58));
        canvas.Children.Add(At(Data("SEAT 27A  GRP 1  " + d.Text("id"), 18, 21, bold: false), 50, tear + 120));
        var stubTitle = Data(d.Title, 22, 22, bold: false);
        ((FrameworkElement)stubTitle).Width = 440;
        canvas.Children.Add(At(stubTitle, 50, tear + 158));
        canvas.Children.Add(At(Aztec(data, 196), w - 246, tear + 44));
        canvas.Children.Add(At(LinearBarcode(data, w - 96, 76), 48, tear + 262));
        canvas.Children.Add(At(Label("0027 4815 1623 4200 " + d.Text("id").Replace("–", "").Replace("-", ""), Courier, 18, FontWeights.Normal, grey, TextAlignment.Left), 48, tear + 350));

        // A single fold across the middle.
        canvas.Children.Add(At(new Border
        {
            Width = w, Height = 34,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, 22), .46), new(Alpha(Colors.White, 120), .54), new(Alpha(Colors.White, 0), 1),
            }, 90),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-1.4),
        }, 0, 690));

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

    // ======================= Human marks =======================

    private static readonly FontFamily HandFont = new(FontFolder, "./#Caveat");
    private static readonly FontFamily Courier = new(FontFolder, "./#Courier Prime");

    /// <summary>
    /// Writing by hand: each line its own block, a little tilted, a little off the rule, a size
    /// that wavers and ink that is not quite even, all from a fixed seed (the same every time).
    /// Line boxes are exactly one rule tall, so the writing sits on ruled paper.
    /// </summary>
    private static FrameworkElement Handwriting(string text, FontFamily font, double size, Color ink, double width, int seed, TextAlignment align,
        double rule, double maxHeight = double.PositiveInfinity, FontWeight? weight = null)
    {
        for (var scale = 1.0; ; scale *= .93)
        {
            var random = new Random(seed);
            var stack = new StackPanel { Width = width };
            foreach (var raw in text.Split('\n'))
            {
                var block = new TextBlock
                {
                    Text = raw, FontFamily = font, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                    FontSize = size * scale * (0.97 + (random.NextDouble() * .06)), Foreground = Brush(Alpha(ink, (byte)(226 + random.Next(29)))),
                    TextAlignment = align, LineHeight = rule, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    Margin = new Thickness((random.NextDouble() * 12) - 2, 0, 0, 0),
                    RenderTransformOrigin = new Point(0, .5), RenderTransform = new RotateTransform((random.NextDouble() * 1.6) - .9),
                };
                stack.Children.Add(block);
            }
            stack.Measure(new Size(width, double.PositiveInfinity));
            if (stack.DesiredSize.Height <= maxHeight || scale < .5)
                return stack;
        }
    }

    /// <summary>Kiosk print: a thermal monospace, a touch off register and unevenly dark.</summary>
    private static FrameworkElement Thermal(string text, double size, Color ink, int seed, bool bold)
    {
        var random = new Random(seed * 7919);
        var block = Label(text, Courier, size, bold ? FontWeights.Bold : FontWeights.Normal, Alpha(ink, (byte)(205 + random.Next(40))), TextAlignment.Left);
        block.TextTrimming = TextTrimming.None;
        block.TextWrapping = TextWrapping.Wrap;
        block.RenderTransformOrigin = new Point(0, .5);
        block.RenderTransform = new TransformGroup
        {
            Children = { new RotateTransform((random.NextDouble() * .7) - .35), new TranslateTransform((random.NextDouble() * 4) - 2, (random.NextDouble() * 3) - 1) },
        };
        block.OpacityMask = Speckle(seed, .82);
        return block;
    }

    private static FrameworkElement ThermalBlock(string text, Color ink, double width, double maxHeight, double start, TextAlignment align)
    {
        var fitted = FitText(text, Courier, FontWeights.Bold, FontStyles.Normal, ink, width, maxHeight, start, 20, align, 1.12);
        fitted.Foreground = Brush(Alpha(ink, 225));
        fitted.OpacityMask = Speckle(5, .8);
        fitted.RenderTransformOrigin = new Point(0, .5);
        fitted.RenderTransform = new RotateTransform(-.3);
        return fitted;
    }

    /// <summary>Uneven ink: a fine, fixed speckle of slightly lighter spots.</summary>
    private static Brush Speckle(int seed, double floor)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 60, 60))));
        var random = new Random(seed);
        var spots = new GeometryGroup();
        for (var i = 0; i < 26; i++)
            spots.Children.Add(new EllipseGeometry(new Point(random.NextDouble() * 60, random.NextDouble() * 60), .6 + random.NextDouble() * 1.6, .5 + random.NextDouble()));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(255 * floor), 0, 0, 0)), null, spots));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 60, 60), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    /// <summary>A rubber stamp: double frame, heavy capitals, a second line, ink that did not take everywhere.</summary>
    private static FrameworkElement RubberStamp(string text, string line, Color color, double angle, int seed)
    {
        var inside = new StackPanel();
        inside.Children.Add(Label(text, Figtree, 54, FontWeights.Black, color, TextAlignment.Center));
        var second = Label(line, Courier, 18, FontWeights.Bold, color, TextAlignment.Center);
        second.Margin = new Thickness(0, -4, 0, 0);
        inside.Children.Add(second);
        var stamp = new Border
        {
            BorderBrush = Brush(color), BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(10), Padding = new Thickness(5),
            Child = new Border { BorderBrush = Brush(color), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Padding = new Thickness(22, 6, 22, 10), Child = inside },
            Opacity = .78, OpacityMask = Speckle(seed, .35),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(angle),
        };
        return stamp;
    }

    /// <summary>A line drawn in pen: round ends, ink a little uneven.</summary>
    private static FrameworkElement Pen(Geometry path, Color ink, double thickness) => new Path
    {
        Data = path, Stroke = Brush(ink), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, Opacity = .92,
    };

    /// <summary>A pencil line: grey, soft, grainy.</summary>
    private static FrameworkElement Pencil(Geometry path, Color graphite, double thickness) => new Path
    {
        Data = path, Stroke = Brush(Alpha(graphite, 190)), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, OpacityMask = Speckle(41, .45),
    };

    /// <summary>Paper tape: translucent, its ends torn in a zigzag.</summary>
    private static FrameworkElement Tape(Color color, double width, double height, double angle)
    {
        var points = new PointCollection();
        for (var y = 0.0; y <= height; y += height / 6)
            points.Add(new Point(((int)(y / (height / 6)) % 2 == 0) ? 0 : 7, y));
        for (var y = height; y >= 0; y -= height / 6)
            points.Add(new Point(width - (((int)(y / (height / 6)) % 2 == 0) ? 0 : 7), y));
        return new Polygon
        {
            Points = points, Fill = Brush(Alpha(color, 150)), Stroke = Brush(Alpha(Darken(color, .8), 60)), StrokeThickness = 1,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(angle),
        };
    }

    /// <summary>A bookmark ribbon hanging from the top edge, its end cut in a V.</summary>
    private static FrameworkElement Ribbon(Color color, double width, double length, HorizontalAlignment align, Thickness margin) => new Path
    {
        Data = Geometry.Parse(FormattableString.Invariant($"M0,0 L{width},0 L{width},{length} L{width / 2},{length - (width * .55)} L0,{length} Z")),
        Fill = new LinearGradientBrush(Lighten(color, .08), Darken(color, .78), 0),
        HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Top, Margin = margin,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Direction = 315, Opacity = .3 },
    };

    /// <summary>Fine interlaced curves, like the security print on tickets.</summary>
    private static FrameworkElement Guilloche(Color color, double width, double height)
    {
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            for (var k = 0; k < 14; k++)
            {
                c.BeginFigure(new Point(0, height / 2), false, false);
                for (var x = 0.0; x <= width; x += 6)
                {
                    var y = (height / 2) + (Math.Sin((x / 70) + (k * .45)) * (height * .38)) * Math.Cos((x / 230) + (k * .2));
                    c.LineTo(new Point(x, y), true, false);
                }
            }
        }
        geometry.Freeze();
        return new Path { Data = geometry, Stroke = Brush(color), StrokeThickness = 1.1, Width = width, Height = height };
    }

    private static double MeasureWidth(string text, FontFamily font, double size)
    {
        var block = new TextBlock { Text = text, FontFamily = font, FontSize = size };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
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
