using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskitify.Views;

public static partial class LyricCardRenderer
{

    private static FrameworkElement Cassette(Studio d)
    {
        var w = d.Wide ? 1390 : 984;
        var shell = C("#2C2B35");
        var label = d.Color("label", C("#E7BB76"));
        var ink = TextOn(label);
        var white = C("#E2D4C3");
        var side = Box(d.TitleText(d.Text("side"), label, 57, Mono, TextAlignment.Center), shell, 7, 5);
        var header = Pair(Stack(d.TitleText(d.Text("title"), ink, 54), d.Small(d.Text("to"), ink, 28, font: DmSerif)), side, .84);
        var note = Box(Stack(header, Space(Rule(Alpha(ink, 160), weight: 5), 17, 22),
            d.Lyrics(w - 181, ink, 72), Space(d.Song(ink, 25), 23)), label, 34, 16);
        note.Background = Fibers(label, Alpha(ink, 35));
        var window = new Grid { Height = d.H(175) };
        window.Children.Add(Recess(new Grid(), C("#171819"), 1, 35));
        var film = new Border { Height = d.H(94), Margin = new Thickness(104, 0, 104, 0),
            Background = new LinearGradientBrush(C("#6A4E43"), C("#262025"), 90), CornerRadius = new CornerRadius(45) };
        window.Children.Add(film);
        var reels = new UniformGrid { Rows = 1, Margin = new Thickness(53, 0, 53, 0) };
        for (var i = 0; i < 2; i++)
        {
            var size = d.H(118);
            var reel = new Grid { Width = size, Height = size };
            reel.Children.Add(new Ellipse { Fill = Brush(C("#AB9880")), Stroke = Brush(C("#171819")), StrokeThickness = 8 });
            reel.Children.Add(new Ellipse { Width = size * .76, Height = size * .76, Stroke = Brush(shell), StrokeThickness = 3 });
            var spokes = new TextBlock { Text = "✳", FontFamily = InterDisplay, FontSize = size * .53, Foreground = Brush(shell),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            reel.Children.Add(spokes);
            reels.Children.Add(reel);
        }
        window.Children.Add(reels);
        var basePlate = Enamel(Pair(Screw(white, 27), Screw(white, 27), .5),
            C("#50505A"), new Thickness(21, 13, 21, 12), new CornerRadius(45, 45, 4, 4));
        var deck = Stack(note, Space(window, d.H(33)), Space(d.Small(d.Text("note"), white, 25, TextAlignment.Center, Mono), 17),
            Space(basePlate, d.H(23)));
        return d.Stage(Mounted(Enamel(deck, shell, new Thickness(42, 35, 42, 24), new CornerRadius(37)), 28),
            C("#403942"), Colors.White, w, Desk(C("#8B6B6A"), C("#2E2734")), inset: 60);
    }


    private static FrameworkElement ConcertTicket(Studio d)
    {
        var w = d.Wide ? 1430 : 943;
        var dusk = C("#2A223E");
        var coral = C("#9B3150");
        var cream = C("#F4D9B5");
        var header = Stack(d.Small(d.Text("title").ToUpperInvariant(), coral, 29, font: Mono),
            Space(d.TitleText(d.Artist, Ink, d.Story ? 103 : 76), 12), d.Small(d.Title, C("#5E374D"), 33));
        var cover = d.Photo(d.Art(d.Story ? 215 : 155), 3);
        FrameworkElement bill = cover is null ? header : Pair(header, Box(cover, coral, 6), .72, 26);
        var printed = Stack(bill, Space(Rule(coral, weight: 6), 25, 33), d.Lyrics(w - 148, Ink, 75),
            Space(Rule(coral, dotted: true), d.H(47), 19),
            Pair(Stack(d.Small("SOUVENIR  /  " + d.Text("id"), coral, 28, font: Mono),
                d.Small(d.Text("date") + (d.Timestamp ? "  ·  " + d.Time : ""), Ink, 24, font: Mono)),
                Barcode(Ink, d.H(71)), .57));
        var ticket = new Grid();
        var paper = Box(printed, cream, 61, 10);
        paper.Background = Fibers(cream, Alpha(coral, 36), 1.15);
        ticket.Children.Add(paper);
        foreach (var end in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
            ticket.Children.Add(new Ellipse { Width = 58, Height = 58, Fill = Brush(dusk),
                HorizontalAlignment = end, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(-31, 0, -31, d.H(120) + 28) });
        var top = Box(d.Small("SINGLE ENTRY     /     MUSIC MEMENTO", cream, 25, TextAlignment.Center, Mono), coral, 17, 6);
        return d.Stage(Stack(top, ticket), dusk, Colors.White, w,
            Desk(C("#5F4167"), dusk), inset: 85, footerGap: 42);
    }


    private static FrameworkElement Receipt(Studio d)
    {
        var w = d.Wide ? 1030 : 809;
        var thermal = C("#F5F0DF");
        var ink = C("#262922");
        var sage = C("#4C655B");
        Canvas Edge(bool flip)
        {
            var edge = new Canvas { Height = 13, ClipToBounds = true };
            for (var i = 0; i < (int)(w / 16) + 1; i++)
            {
                var tooth = new Polygon { Fill = Brush(thermal), Points = flip
                    ? new PointCollection([new(0, 13), new(16, 13), new(8, 0)])
                    : new PointCollection([new(0, 0), new(16, 0), new(8, 13)]) };
                Canvas.SetLeft(tooth, i * 16);
                edge.Children.Add(tooth);
            }
            return edge;
        }
        var totals = Pair(d.Small(d.Timestamp ? "TIME\n" + d.Time : "", sage, 24, font: Mono),
            d.Small("DURATION\n" + d.Duration, ink, 25, TextAlignment.Right, Mono));
        var paper = Box(Stack(d.Small(d.Text("shop").ToUpperInvariant(), sage, 26, TextAlignment.Center, Mono),
            Space(d.TitleText(d.Text("title"), ink, 51, Mono, TextAlignment.Center), 17, 21),
            Pair(d.Small("NO. " + d.Text("id"), ink, 26, font: Mono), d.Small(d.Text("date"), ink, 25, TextAlignment.Right, Mono)),
            Space(Rule(ink, true), 20, 29), d.TitleText(d.Title, ink, 41, Mono), d.Small(d.Artist, sage, 29, font: Mono),
            Space(d.Lyrics(w - 131, ink, 74, face: TerminalFace), d.H(41)),
            Space(Rule(ink, true), 23, 18), totals,
            Space(d.Small(d.Text("extra"), sage, 25, font: Mono), 20), Space(Barcode(ink, d.H(71)), d.H(36)),
            Space(d.Small(d.Text("note"), ink, 28, TextAlignment.Center, Mono), 19)), thermal, 58);
        paper.Background = Fibers(thermal, Alpha(sage, 33), .85);
        return d.Stage(Stack(Edge(true), paper, Edge(false)), C("#304E49"), Colors.White, w,
            Desk(C("#599085"), C("#273E3C")), inset: 62, footerGap: 31);
    }


    private static FrameworkElement BoardingPass(Studio d)
    {
        var w = d.Wide ? 1440 : 977;
        var navy = C("#173A53");
        var acid = C("#D3E79B");
        var paper = C("#EFECE2");
        var stub = Box(Stack(d.Small("JOURNEY", acid, 22, font: Mono),
            Space(d.TitleText(d.Text("id"), Colors.White, 37, Mono), 13), Space(d.Small("DATE", acid, 22, font: Mono), 29),
            d.Small(d.Text("date"), Colors.White, 27, font: Mono),
            Space(Barcode(acid, d.H(96)), d.H(51))), navy, 21, 5);
        var route = Pair(Stack(d.Small("ORIGIN", navy, 22), d.TitleText(d.Text("from"), navy, 64)),
            Stack(d.Small("DESTINATION   ↗", navy, 22, TextAlignment.Right),
                d.TitleText(d.Text("to"), navy, 64, align: TextAlignment.Right)), .5, 16);
        var passenger = Box(Stack(route, Space(Rule(navy, true), 21, 23),
            d.Lyrics(w * .73 - 85, Ink, 70), Space(d.Song(navy, 25), 23),
            Space(d.Small(d.Text("note"), navy, 25), 13)), paper, 36, 5);
        passenger.Background = Fibers(paper, Alpha(navy, 25));
        var chrome = Box(Pair(d.Small("BOARDING PASS  /  IMAGINARY TRAVEL", navy, 27, font: Mono),
            d.Small("GATE   ∞", navy, 26, TextAlignment.Right, Mono), .76), acid, 24, 6);
        var pass = Box(Stack(chrome, Space(Pair(passenger, stub, .74, 9), 8)), C("#7CA0AE"), 8, 8);
        return d.Stage(Mounted(pass, 28), C("#527F90"), Colors.White, w,
            LinedPaper(C("#527F90"), C("#6594A5"), 68, grid: true), inset: 83);
    }


    private static FrameworkElement Library(Studio d)
    {
        var w = d.Wide ? 1220 : 969;
        var card = C("#EED9A9");
        var dark = C("#395A55");
        var red = C("#9F4736");
        var title = Stack(d.Small(d.Text("library").ToUpperInvariant(), dark, 31, font: Mono),
            Space(d.TitleText(d.Title, Ink, 72, DmSerif), 13));
        var metadata = Pair(Stack(d.Small("AUTHOR", red, 22, font: Mono), d.Small(d.Artist, dark, 31)),
            Stack(d.Small("SECTION", red, 22, font: Mono), d.Small(d.Text("category"), dark, 30)), .53, 18);
        var dates = Pair(Stack(d.Small("BORROWED", red, 22, font: Mono), d.Small(d.Text("date"), dark, 30, font: Mono)),
            Stack(d.Small("RETURN DATE", red, 22, font: Mono), d.Small(d.Text("return"), dark, 30, font: Mono)), .53, 18);
        var form = Stack(title, Space(metadata, 22), Space(Rule(dark, weight: 6), 28, 23),
            d.Lyrics(w - 143, Ink, 75), Space(Rule(dark), d.H(49), 20), dates, Space(Stamp(d.Text("stamp"), red, 32), 30));
        var paper = Box(form, card, 64, 8, C("#B59B71"));
        paper.Background = LinedPaper(card, C("#CEC09A"), d.H(63));
        return d.Stage(Mounted(paper, 25), C("#6D5045"), Colors.White, w,
            Desk(C("#9F7B62"), C("#503B36")), inset: 77, footerGap: 35);
    }


    private static FrameworkElement Passport(Studio d)
    {
        var w = d.Wide ? 1340 : 950;
        var teal = C("#1B4552");
        var paper = C("#EEE4C9");
        var gold = C("#D6B473");
        var rust = C("#914E45");
        var emblem = Box(d.Small("◇", gold, 64, TextAlignment.Center), Darken(teal, .79), 4, 52, gold);
        emblem.Width = 106; emblem.Height = d.H(94);
        emblem.HorizontalAlignment = HorizontalAlignment.Center;
        var name = Stack(d.Small(d.Text("country").ToUpperInvariant(), gold, 25, TextAlignment.Center, Mono),
            Space(emblem, 17), Space(d.TitleText("MUSIC PASSPORT", gold, 63, DmSerif, TextAlignment.Center), 16),
            Space(d.Small("FICTIONAL TRAVEL DOCUMENT", gold, 22, TextAlignment.Center, Mono), 17));
        var info = Stack(d.TitleText(d.Title, Ink, 54, DmSerif), d.Small(d.Artist, teal, 27),
            Space(d.Small("ORIGIN   " + d.Text("origin"), teal, 27, font: Mono), 15));
        var portrait = d.Photo(d.Art(155), 3);
        FrameworkElement bio = portrait is null ? info : Pair(Box(portrait, teal, 5, 3, gold), info, .27, 25);
        var dates = Pair(d.Small("ISSUED   " + d.Text("issue"), teal, 24, font: Mono),
            d.Small("EXPIRES   " + d.Text("expire"), teal, 24, TextAlignment.Right, Mono));
        var lyrics = d.Lyrics(d.Wide ? w * .67 - 133 : w - 169, Ink, 71);
        var page = Box(Stack(bio, Space(Rule(gold, true), 20, 20), lyrics, Space(dates, 26),
            Space(Stamp(d.Text("stamp"), rust, 29), 22), Space(d.Small(d.Text("note"), teal, 24), 14)), paper, 43, 5);
        page.Background = LinedPaper(paper, Alpha(gold, 72), 57, grid: true);
        var cover = Box(name, teal, 34, 11, gold);
        var spread = d.Wide ? Pair(cover, page, .33, 12) : Stack(cover, Space(page, 8));
        var booklet = Enamel(spread, Darken(teal, .74), new Thickness(21), new CornerRadius(27));
        return d.Stage(Mounted(booklet, 24), C("#413743"), Colors.White, w, Desk(C("#79717A"), C("#302933")), inset: 49);
    }


    private static FrameworkElement Classified(Studio d)
    {
        var w = d.Wide ? 1300 : 963;
        var sheet = C("#E4D9C6");
        var red = C("#A23D38");
        var file = C("#9D7552");
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var width in new[] { 96d, 176, 69 })
            strip.Children.Add(new Rectangle { Width = width, Height = 23, Fill = Brush(Ink), Margin = new Thickness(0, 0, 14, 0) });
        var head = Pair(d.Small(d.Text("dept").ToUpperInvariant(), Ink, 28, font: Mono),
            d.Small(d.Text("id"), red, 28, TextAlignment.Right, Mono), .72);
        var memo = Stack(head, Space(Rule(Ink, weight: 3), 17, 25),
            Stamp(d.Text("class"), red, 53), Space(strip, 28, 21), d.Song(Ink, 32),
            Space(d.Small(d.Text("note").ToUpperInvariant(), red, 28, font: Mono), d.H(37), 15),
            d.Lyrics(w - 190, Ink, 73, face: TerminalFace),
            Space(Rule(red, true), d.H(38), 15),
            Pair(Stamp(d.Text("stamp"), red, 23), d.Small(d.Text("footer"), Ink, 24, TextAlignment.Right, Mono), .38));
        var paper = Box(memo, sheet, 54, 1);
        paper.Background = Fibers(sheet, Alpha(file, 38), 1);
        var tab = Box(d.Small("FILE  " + d.Text("id"), Ink, 25, font: Mono), file, 22, 7);
        tab.HorizontalAlignment = HorizontalAlignment.Left;
        var folder = Stack(tab, Box(paper, file, 27, 7));
        return d.Stage(folder, C("#443F41"), Colors.White, w, Desk(C("#766A64"), C("#393438")), inset: 55);
    }

    private static FrameworkElement DictionaryPage(Studio d)
    {
        var w = d.Wide ? 1360 : 988;
        var cream = C("#FAF5E9");
        var vermilion = C("#A14A3F");
        var grammatical = string.Join("  ·  ", new[] { d.Text("class"), d.Text("category") }.Where(item => !string.IsNullOrWhiteSpace(item)));
        var word = d.TitleText(d.Text("word"), Ink, d.Story ? 116 : 91, DmSerif);
        var index = d.Small(d.Text("number") + ".", vermilion, 44, font: DmSerif);
        var body = new DockPanel { LastChildFill = true };
        index.Width = 80;
        index.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(index, Dock.Left);
        body.Children.Add(index);
        body.Children.Add(d.Lyrics(w - 222, Ink, 74));
        var page = Stack(Pair(d.Small("THE LISTENER'S LEXICON", vermilion, 25, font: Mono), d.Small("DEFINITIONS  /  027", vermilion, 23, TextAlignment.Right, Mono), .56),
            Space(Rule(vermilion, weight: 3), 15, 25), word);
        if (grammatical.Length > 0)
            page.Children.Add(Space(d.Small(grammatical, vermilion, 30, font: DmSerif), 12));
        page.Children.Add(Space(body, d.H(60)));
        var credit = d.Text("note");
        if (!string.IsNullOrWhiteSpace(credit))
            page.Children.Add(Space(d.Small("— " + credit, vermilion, 29, font: DmSerif), 30));
        var leaf = Box(page, cream, 69, 1);
        leaf.Background = Fibers(cream, Alpha(vermilion, 28), 1.2);
        leaf.BorderBrush = Brush(C("#D2BBA2"));
        leaf.BorderThickness = new Thickness(11, 1, 1, 1);
        return d.Stage(Mounted(leaf, 23), C("#A29080"), Ink, w,
            Desk(C("#D0BFA5"), C("#8F7C74")), inset: 89, footerGap: 39);
    }

    private static FrameworkElement Book(Studio d)
    {
        var w = d.Wide ? 1210 : 954;
        var vanilla = C("#F7ECD4");
        var accent = C("#886047");
        var dropInitial = d.Text("initial") == "On";
        var highlighted = d.Text("highlight") == "On";
        const double initialWidth = 110;
        var verso = d.Lyrics(w - 168 - (dropInitial ? initialWidth : 0) - (highlighted ? 16 : 0), Ink, 73, dropInitial: dropInitial);
        FrameworkElement body = verso;
        if (highlighted)
            body = Box(verso, C("#ECD18B"), 8, 3);
        if (dropInitial && FirstInitial(d) is { } initial)
        {
            // The first grapheme is typeset in the margin, beside the rest of the verse.
            var row = new DockPanel();
            initial.Width = initialWidth;
            initial.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(initial, Dock.Left);
            row.Children.Add(initial);
            row.Children.Add(body);
            body = row;
        }
        var page = Stack(d.Small(d.Text("chapter").ToUpperInvariant(), accent, 28, TextAlignment.Center),
            Space(d.TitleText(d.Title, Ink, 82, DmSerif, TextAlignment.Center), d.H(56)),
            Space(d.Small("❧", accent, 36, TextAlignment.Center), 13),
            Space(body, d.H(55)), Space(d.Small(d.Text("note"), accent, 25, TextAlignment.Right, DmSerif), 25),
            Space(d.Small("—  " + d.Text("page") + "  —", accent, 28, TextAlignment.Center, DmSerif), d.H(48)));
        var book = Box(page, vanilla, 77, 2);
        book.Background = Fibers(vanilla, Alpha(accent, 31), .85);
        book.BorderThickness = new Thickness(17, 1, 2, 1);
        book.BorderBrush = new LinearGradientBrush(C("#9B7C65"), C("#DBCCAA"), 8);
        return d.Stage(Mounted(book, 25), C("#554344"), Colors.White, w,
            Desk(C("#A78072"), C("#4B3B43")), inset: 75);
    }

    private static TextBlock? FirstInitial(Studio d)
    {
        if (d.Initial is not { Length: > 0 } initial) return null;
        return d.TitleText(initial, C("#966347"), 85, DmSerif);
    }

    private static FrameworkElement ElementTile(Studio d)
    {
        var w = d.Wide ? 1450 : 970;
        var board = C("#173732");
        var bright = C("#D7ECAB");
        var tileColor = C("#285A4C");
        var symbol = d.Text("symbol");
        if (string.IsNullOrWhiteSpace(symbol))
        {
            var words = d.Title.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            symbol = words.Length == 0 ? "♪" : StringInfo.GetNextTextElement(words[0]).ToUpperInvariant()
                + (words.Length > 1 ? StringInfo.GetNextTextElement(words[1]).ToLowerInvariant() : "");
        }
        var tile = Box(Stack(d.Small(d.Text("id") + "  /  FICTIONAL ELEMENT", bright, 24, font: Mono),
            d.TitleText(symbol, bright, d.H(d.Story ? 204 : 151)), d.TitleText(d.Title, bright, 46), d.Small(d.Artist, bright, 26)), tileColor, 39, 4, bright);
        var values = new[] { ("Classification", d.Text("class")), ("State", d.Text("state")), ("Half-life", d.Text("half")) }
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2));
        var properties = d.Small(string.Join("     ", values.Select(pair => pair.Item1 + ": " + pair.Item2)), bright, 25, font: Mono);
        var annotation = Box(Stack(properties, Space(d.Small(d.Text("note").ToUpperInvariant(), bright, 25, font: Mono), 26, 18),
            d.Lyrics(d.Wide ? w * .57 - 112 : w - 107, Colors.White, 73)), C("#214741"), 39, 5, C("#5B8066"));
        var plate = d.Wide ? Pair(tile, annotation, .43, 24) : Stack(tile, Space(annotation, d.H(35)));
        var lab = Stack(Pair(d.Small("ARCHIVE OF LISTENING", bright, 25, font: Mono),
            d.Small(d.Text("id"), bright, 25, TextAlignment.Right, Mono)), Space(plate, 23));
        return d.Stage(lab, board, bright, w, LinedPaper(board, C("#2E544C"), 68, grid: true), inset: 66);
    }

    private static FrameworkElement Crossword(Studio d)
    {
        var w = d.Wide ? 1380 : 982;
        var cream = C("#EDE8DB");
        var dusk = C("#343D55");
        var highlight = C("#DBAA61");
        var letters = StringInfo.GetTextElementEnumerator(d.Text("word").ToUpperInvariant());
        var word = new List<string>();
        while (letters.MoveNext() && word.Count < 18)
        {
            var grapheme = letters.GetTextElement();
            if (grapheme.Any(char.IsLetterOrDigit))
                word.Add(grapheme);
        }
        if (word.Count == 0)
            word.Add("♪");
        var cols = Math.Max(6, word.Count);
        const int rows = 5;
        var grid = new UniformGrid { Rows = rows, Columns = cols, Width = w - 121, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < rows * cols; i++)
        {
            var index = i - cols * 2;
            var text = index >= 0 && index < word.Count ? word[index] : "";
            var dark = text.Length == 0 && ((i / cols + i % cols) % 4 == 0 || i % cols == cols - 1);
            var cell = Box(d.Small(text, dusk, Math.Min(35, (w - 112) / cols * .69), TextAlignment.Center, Mono), text.Length > 0 ? highlight : dark ? dusk : cream, 0);
            cell.Height = d.H(66);
            cell.Margin = new Thickness(1);
            grid.Children.Add(cell);
        }
        var puzzle = Stack(Pair(d.TitleText("CROSSWORD", dusk, 65), d.Small(d.Text("id"), dusk, 24, TextAlignment.Right, Mono), .67),
            Space(grid, d.H(24)), Space(d.Small("LYRIC CLUE  /  DECORATIVE GRID", dusk, 24, font: Mono), 28, 14),
            d.Lyrics(w - 120, Ink, 72), Space(d.Small(d.Text("clue"), dusk, 24), 18), Space(d.Song(dusk, 25), 8));
        var magazine = Box(puzzle, cream, 53, 1);
        magazine.Background = Fibers(cream, Alpha(dusk, 31));
        return d.Stage(Mounted(magazine, 22), C("#50607C"), Colors.White, w,
            Desk(C("#8F9EAF"), C("#414C68")), inset: 62);
    }
}
