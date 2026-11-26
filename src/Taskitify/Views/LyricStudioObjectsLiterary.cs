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
        var w = Math.Min(d.Width, d.Wide ? 1260 : 910);
        var shell = C("#1C2029");
        var label = d.Color("label", C("#EBBB77"));
        var ink = TextOn(label);
        var inkMuted = Alpha(ink, 0xB8);
        var reelWindow = new Grid { Height = d.H(137) };
        var hubs = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(46, 0, 46, 0) };
        for (var i = 0; i < 2; i++)
        {
            var reel = new Grid { Width = d.H(100), Height = d.H(100) };
            reel.Children.Add(new Ellipse { Fill = Brush(C("#CCBCAE")), Stroke = Brush(Colors.Black), StrokeThickness = 8 });
            var cog = new Border { Width = d.H(54), Height = d.H(54), Child = d.Small("✳", shell, 47, TextAlignment.Center) };
            reel.Children.Add(cog);
            hubs.Children.Add(reel);
        }
        reelWindow.Children.Add(new Border { Background = Brush(C("#0D1113")), CornerRadius = new CornerRadius(46), BorderThickness = new Thickness(5), BorderBrush = Brush(C("#545A66")) });
        reelWindow.Children.Add(hubs);
        var header = Pair(d.TitleText(d.Text("title"), ink, 54), d.TitleText(d.Text("side"), ink, 57, Mono, TextAlignment.Right), .83);
        var dedication = Box(Stack(header, d.Small(d.Text("to"), inkMuted, 26), Space(Rule(inkMuted, weight: 4), 15, 20),
            d.Lyrics(w - 166, ink, 63), Space(d.Song(inkMuted, 26), 15)), label, 31, 18);
        var assembly = Stack(dedication, Space(reelWindow, d.H(26)), Space(Pair(d.Small("●  " + d.Text("note"), C("#D9DCE5"), 25, font: Mono),
            d.Small("●", C("#D9DCE5"), 24, TextAlignment.Right), .88), 22));
        return d.Stage(Box(assembly, shell, 41, 36, C("#696C76")), C("#5B4950"), Colors.White, w,
            new LinearGradientBrush(C("#342C38"), C("#815F60"), 95));
    }

    private static FrameworkElement ConcertTicket(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1290 : 880);
        var outer = C("#241D38");
        var coral = C("#AB3654");
        var cream = C("#F9DFC6");
        var header = Stack(d.Small(d.Text("title").ToUpperInvariant(), coral, 26), d.TitleText(d.Artist, Ink, 62), d.Small(d.Title, ink: C("#613750"), 31));
        FrameworkElement intro = d.Photo(d.Art(144), 3) is { } photo ? Pair(header, photo, .74) : header;
        var main = Stack(intro, Space(Rule(coral, true), 26, 24), d.Lyrics(w - 142, Ink, 68));
        var stubInfo = Stack(d.Small("SOUVENIR  /  " + d.Text("id"), coral, 25, font: Mono),
            d.Small(d.Text("date") + (d.Timestamp ? "  ·  " + d.Time : ""), Ink, 25, font: Mono));
        var ticket = new Grid();
        ticket.Children.Add(Box(Stack(main, Space(Rule(coral, true), 24, 15), Pair(stubInfo, Barcode(Ink, d.H(51)), .55)),
            cream, 56, 15));
        foreach (var alignment in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
        {
            var punch = new Ellipse { Width = 51, Height = 51, Fill = Brush(outer), HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-25, 0, -25, d.H(116)) };
            ticket.Children.Add(punch);
        }
        return d.Stage(ticket, outer, Colors.White, w);
    }

    private static FrameworkElement Receipt(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1030 : 760);
        var thermal = C("#F7F2E8");
        var fg = C("#262B28");
        var accent = C("#5D7268");
        var saw = new Canvas { Height = 15 };
        for (var i = 0; i < (int)(w / 16) + 1; i++)
        {
            var tooth = new Polygon { Fill = Brush(thermal), Points = new PointCollection([new(0, 0), new(16, 0), new(8, 15)]) };
            Canvas.SetLeft(tooth, i * 16); saw.Children.Add(tooth);
        }
        var date = Pair(d.Small("NO. " + d.Text("id"), fg, 25, font: Mono), d.Small(d.Text("date"), fg, 25, TextAlignment.Right, Mono));
        var transaction = Stack(d.Small(d.Text("shop"), accent, 26, TextAlignment.Center, Mono),
            Space(d.TitleText(d.Text("title"), fg, 48, Mono, TextAlignment.Center), 10, 16),
            date, Space(Rule(accent, true), 16, 17), d.TitleText(d.Title, fg, 36, Mono), d.Small(d.Artist, fg, 28, font: Mono),
            Space(d.Lyrics(w - 102, fg, 67, face: TerminalFace), 25),
            Space(Rule(accent, true), 18, 14),
            d.Small((d.Timestamp ? "TIME  " + d.Time + "       " : "") + "DURATION  " + d.Duration, fg, 25, font: Mono),
            Space(d.Small(d.Text("extra"), accent, 23, font: Mono), 13),
            Space(Barcode(fg, d.H(60)), 26),
            Space(d.Small(d.Text("note"), fg, 27, TextAlignment.Center, Mono), 20));
        return d.Stage(Stack(Box(transaction, thermal, 48), saw), C("#33544C"), Colors.White, w);
    }

    private static FrameworkElement BoardingPass(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1340 : 885);
        var navy = C("#133A58");
        var lime = C("#CFEB97");
        var cream = C("#F4F4EC");
        var from = Stack(d.Small("ORIGIN", navy, 23), d.TitleText(d.Text("from"), navy, 58));
        var destination = Stack(d.Small("DESTINATION", navy, 23, TextAlignment.Right), d.TitleText(d.Text("to"), navy, 58, align: TextAlignment.Right));
        var route = new Grid();
        route.Children.Add(Pair(from, destination, .5, 76));
        route.Children.Add(new TextBlock { Text = "→", Foreground = Brush(navy), FontSize = 49, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        var body = Stack(route, Space(Rule(navy, true), 26, 21), d.Lyrics(w - 111, Ink, 67),
            Space(d.Song(navy, 26), 22), Space(d.Small(d.Text("note"), navy, 25), 12));
        var head = Box(Pair(d.Small("BOARDING PASS   /   IMAGINARY TRAVEL", navy, 24, font: Mono), d.Small(d.Text("id"), navy, 27, TextAlignment.Right, Mono), .74), lime, 22, 9);
        var stub = Box(Pair(d.Small(d.Text("date"), cream, 24, font: Mono), Barcode(cream, 32), .5), navy, 18, 8);
        return d.Stage(Stack(head, Box(body, cream, 53), stub), C("#6D90A4"), Colors.White, w, LinedPaper(C("#6D90A4"), C("#87A4B6"), 57, grid: true));
    }

    private static FrameworkElement Library(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1060 : 830);
        var card = C("#E9D5A7");
        var green = C("#375E55");
        var ox = C("#A24E3C");
        var intro = Stack(d.Small(d.Text("library").ToUpperInvariant(), green, 28, font: Mono),
            Space(d.TitleText(d.Title, Ink, 62, DmSerif), 10), d.Small("AUTHOR  " + d.Artist, green, 28, font: Mono),
            d.Small("SECTION  " + d.Text("category"), green, 24, font: Mono));
        var grid = Box(Stack(intro, Space(Rule(green, weight: 4), 24, 20), d.Lyrics(w - 126, Ink, 68),
            Space(Pair(d.Small("BORROWED\n" + d.Text("date"), green, 25, font: Mono), d.Small("RETURN DATE\n" + d.Text("return"), green, 25, TextAlignment.Right, Mono)), 28),
            Space(Stamp(d.Text("stamp"), ox, 30), 15)), card, 59, 8);
        grid.Background = LinedPaper(card, C("#C8BA96"), 62);
        return d.Stage(grid, C("#714F41"), Colors.White, w);
    }

    private static FrameworkElement Passport(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1190 : 875);
        var cover = C("#1C4754");
        var paper = C("#F4E9CC");
        var brass = C("#CBAD6D");
        var rust = C("#98524B");
        var name = Stack(d.Small(d.Text("country").ToUpperInvariant(), brass, 28, TextAlignment.Center),
            Space(d.TitleText("MUSIC PASSPORT", brass, 60, DmSerif, TextAlignment.Center), 12),
            Space(d.Small("◇  FICTIONAL TRAVEL DOCUMENT  ◇", brass, 23, TextAlignment.Center, Mono), 10));
        var info = Stack(d.TitleText(d.Title, Ink, 49, DmSerif), d.Small(d.Artist, Ink, 26), Space(d.Small("ORIGIN  " + d.Text("origin"), cover, 25, font: Mono), 10));
        var data = d.Photo(d.Art(148), 3) is { } photo ? Pair(photo, info, .26) : info;
        var page = Stack(data, Space(Rule(brass, true), 21, 22), d.Lyrics(w - 152, Ink, 68),
            Space(Pair(d.Small("ISSUED\n" + d.Text("issue"), cover, 24, font: Mono), d.Small("EXPIRES\n" + d.Text("expire"), cover, 24, TextAlignment.Right, Mono)), 23),
            Space(Stamp(d.Text("stamp"), rust), 23), Space(d.Small(d.Text("note"), cover, 23), 16));
        return d.Stage(Box(Stack(Box(name, cover, 37, 16, brass), Space(Box(page, paper, 46, 5), 14)), cover, 24, 26, brass),
            C("#292A38"), Colors.White, w);
    }

    private static FrameworkElement Classified(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1220 : 880);
        var sheet = C("#E6DCD1");
        var red = C("#9E383A");
        var redact = new StackPanel { Height = 22, Orientation = Orientation.Horizontal };
        foreach (var width in new[] { 89.0, 160, 71 })
            redact.Children.Add(new Rectangle { Width = width, Fill = Brush(Ink), Margin = new Thickness(0, 0, 13, 0) });
        var cover = Stack(Pair(d.Small(d.Text("dept").ToUpperInvariant(), Ink, 25, font: Mono), d.Small("FILE " + d.Text("id"), red, 26, TextAlignment.Right, Mono), .65),
            Space(Stamp(d.Text("class"), red, 40), d.H(40)), Space(redact, 29),
            Space(d.Song(Ink, 34), 24), Space(d.Small(d.Text("note").ToUpperInvariant(), red, 24, font: Mono), 18, 12),
            d.Lyrics(w - 132, Ink, 66, face: TerminalFace),
            Space(Rule(red, true), 21, 15), Pair(Stamp(d.Text("stamp"), red, 23), d.Small(d.Text("footer"), Ink, 22, TextAlignment.Right, Mono), .38));
        return d.Stage(Box(cover, sheet, 61, 2), C("#3D3938"), Colors.White, w);
    }

    private static FrameworkElement DictionaryPage(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1170 : 900);
        var cream = C("#FAF5E9");
        var vermilion = C("#A14A3F");
        var grammatical = string.Join("  ·  ", new[] { d.Text("class"), d.Text("category") }.Where(item => !string.IsNullOrWhiteSpace(item)));
        var word = d.TitleText(d.Text("word"), Ink, d.Wide ? 94 : 82, DmSerif);
        var index = d.Small(d.Text("number") + ".", vermilion, 44, font: DmSerif);
        var body = new DockPanel { LastChildFill = true };
        index.Width = 80;
        index.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(index, Dock.Left);
        body.Children.Add(index);
        body.Children.Add(d.Lyrics(w - 222, Ink, 69));
        var page = Stack(Pair(d.Small("THE LISTENER'S LEXICON", vermilion, 25, font: Mono), d.Small("DEFINITIONS  /  027", vermilion, 23, TextAlignment.Right, Mono), .56),
            Space(Rule(vermilion, weight: 3), 15, 25), word);
        if (grammatical.Length > 0)
            page.Children.Add(Space(d.Small(grammatical, vermilion, 30, font: DmSerif), 12));
        page.Children.Add(Space(body, d.H(60)));
        var credit = d.Text("note");
        if (!string.IsNullOrWhiteSpace(credit))
            page.Children.Add(Space(d.Small("— " + credit, vermilion, 29, font: DmSerif), 30));
        return d.Stage(Box(page, cream, 66, 2), C("#B9A491"), Ink, w);
    }

    private static FrameworkElement Book(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1140 : 865);
        var vanilla = C("#F7ECD4");
        var accent = C("#886047");
        var dropInitial = d.Text("initial") == "On";
        var highlighted = d.Text("highlight") == "On";
        const double initialWidth = 110;
        var verso = d.Lyrics(w - 150 - (dropInitial ? initialWidth : 0) - (highlighted ? 16 : 0), Ink, 68, dropInitial: dropInitial);
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
            Space(d.TitleText(d.Title, Ink, 71, DmSerif, TextAlignment.Center), d.H(48)),
            Space(d.Small("❧", accent, 36, TextAlignment.Center), 13),
            Space(body, d.H(55)), Space(d.Small(d.Text("note"), accent, 25, TextAlignment.Right, DmSerif), 25),
            Space(d.Small("—  " + d.Text("page") + "  —", accent, 28, TextAlignment.Center, DmSerif), d.H(48)));
        var book = Box(page, vanilla, 71);
        book.BorderThickness = new Thickness(10, 0, 0, 0);
        book.BorderBrush = Brush(C("#CCBA99"));
        return d.Stage(book, C("#554344"), Colors.White, w);
    }

    private static TextBlock? FirstInitial(Studio d)
    {
        if (d.Initial is not { Length: > 0 } initial) return null;
        return d.TitleText(initial, C("#966347"), 85, DmSerif);
    }

    private static FrameworkElement ElementTile(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1260 : 860);
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
            d.TitleText(symbol, bright, d.H(142)), d.TitleText(d.Title, bright, 46), d.Small(d.Artist, bright, 26)), tileColor, 39, 4, bright);
        var values = new[] { ("Classification", d.Text("class")), ("State", d.Text("state")), ("Half-life", d.Text("half")) }
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2));
        var properties = d.Small(string.Join("     ", values.Select(pair => pair.Item1 + ": " + pair.Item2)), bright, 25, font: Mono);
        return d.Stage(Stack(tile, Space(properties, 21), Space(d.Small(d.Text("note").ToUpperInvariant(), bright, 23, font: Mono), d.H(44), 12),
            d.Lyrics(w - 60, Colors.White, 70)), board, bright, w, LinedPaper(board, C("#28534A"), 46, grid: true));
    }

    private static FrameworkElement Crossword(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1240 : 905);
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
        const int rows = 3;
        var grid = new UniformGrid { Rows = rows, Columns = cols, Width = Math.Min(w - 112, cols * d.H(79)), HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < rows * cols; i++)
        {
            var index = i - cols;
            var text = index >= 0 && index < word.Count ? word[index] : "";
            var dark = text.Length == 0 && (i + i / cols) % 4 == 0;
            var cell = Box(d.Small(text, dusk, Math.Min(35, (w - 112) / cols * .69), TextAlignment.Center, Mono), text.Length > 0 ? highlight : dark ? dusk : cream, 0);
            cell.Height = d.H(55);
            cell.Margin = new Thickness(1);
            grid.Children.Add(cell);
        }
        var puzzle = Stack(Pair(d.TitleText("CROSSWORD", dusk, 53), d.Small(d.Text("id"), dusk, 24, TextAlignment.Right, Mono), .67),
            Space(grid, d.H(24)), Space(d.Small("LYRIC CLUE  /  DECORATIVE GRID", dusk, 24, font: Mono), 28, 14),
            d.Lyrics(w - 100, Ink, 67), Space(d.Small(d.Text("clue"), dusk, 24), 18), Space(d.Song(dusk, 25), 8));
        return d.Stage(Box(puzzle, cream, 46, 2), C("#6D7895"), Colors.White, w);
    }
}
