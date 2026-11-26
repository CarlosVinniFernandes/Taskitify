using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskitify.Views;

public static partial class LyricCardRenderer
{
    private static Brush LinedPaper(Color paper, Color rule, double period = 64, bool grid = false)
    {
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(Brush(paper), null, new RectangleGeometry(new Rect(0, 0, period, period))));
        var strokes = new GeometryGroup();
        strokes.Children.Add(new LineGeometry(new Point(0, period - 2), new Point(period, period - 2)));
        if (grid) strokes.Children.Add(new LineGeometry(new Point(period - 2, 0), new Point(period - 2, period)));
        tile.Children.Add(new GeometryDrawing(null, new Pen(Brush(rule), 1.5), strokes));
        return new DrawingBrush(tile) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, period, period), ViewportUnits = BrushMappingMode.Absolute };
    }

    private static FrameworkElement PhotoBooth(Studio d)
    {
        var shell = C("#EEEAE1");
        var w = Math.Min(d.Width, d.Wide ? 1180 : 870);
        var head = Pair(d.Small("AUTOMATIC PHOTO LAB", Ink, 25, font: Mono), d.Small(d.Text("ref"), Ink, 24, TextAlignment.Right, Mono), .46);
        var verses = Stack(d.TitleText(d.Text("title"), Ink, 48), Space(d.Lyrics(w - 104, Ink, 69), 24), Space(d.Song(C("#625C55")), 18));
        var body = Stack(head, Space(Rule(C("#A6A096")), 14, 18));
        if (d.Photo(d.Art(460), radius: 8) is { } photo)
            body.Children.Add(photo);
        body.Children.Add(Space(verses, 29));
        var print = Box(body, shell, 50, 3);
        print.BorderBrush = Brush(C("#CEC8BA"));
        print.BorderThickness = new Thickness(3);
        return d.Stage(print, Darken(d.Accent, .6), Colors.White, w);
    }

    private static FrameworkElement Letter(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1220 : 840);
        var page = Stack(Pair(d.Small("TO  " + d.Text("to"), C("#7A524B"), 28), d.Small(d.Text("date"), C("#7A524B"), 29, TextAlignment.Right)),
            Space(d.TitleText(d.Text("hello"), Ink, 56, DmSerif), d.H(72)),
            Space(d.Lyrics(w - 152, Ink, 65), 25),
            Space(d.Small(d.Text("bye"), Ink, 35, font: DmSerif), d.H(52)),
            d.TitleText(d.Text("sign"), C("#8F5145"), 51, DmSerif), Space(d.Song(C("#6F5549"), 24), 18));
        var sheet = Box(page, Paper, 72, 2);
        sheet.Background = LinedPaper(Paper, C("#D9C7B8"), d.H(68));
        sheet.BorderBrush = Brush(C("#AF6255"));
        sheet.BorderThickness = new Thickness(7, 0, 0, 0);
        return d.Stage(sheet, C("#5C3E38"), Colors.White, w, new LinearGradientBrush(C("#573834"), C("#90685A"), 48));
    }

    private static FrameworkElement FilmNegative(Studio d)
    {
        var bg = C("#241E1D");
        var ink = C("#F6E1C2");
        var amber = C("#D99A60");
        const double rail = 43;
        var w = Math.Min(d.Width, d.Wide ? 1150 : 900);
        var inside = w - rail * 2 - 66;
        StackPanel Perforations()
        {
            var marks = new StackPanel { Width = rail, Margin = new Thickness(8, 12, 8, 12) };
            for (var i = 0; i < (d.Wide ? 7 : 10); i++)
                marks.Children.Add(Space(new Border { Width = 24, Height = d.H(33), Background = Brush(C("#AD8166")), CornerRadius = new CornerRadius(5) }, d.H(48)));
            return marks;
        }
        var frames = Stack(Pair(d.Small(d.Text("stock"), amber, 25, font: Mono), d.Small(d.Text("frame") + " ▶", amber, 28, TextAlignment.Right, Mono)));
        if (d.Photo(d.Art(370), radius: 6) is { } photo)
            frames.Children.Add(Space(photo, 18));
        frames.Children.Add(Space(Box(Stack(d.Lyrics(inside - 64, ink, 65), Space(d.Song(amber), 22)), C("#332722"), 29, 6, C("#7E5541")), 18));
        frames.Children.Add(Space(d.Small(d.Text("note"), amber, 23, TextAlignment.Right, Mono), 20));
        var strip = new DockPanel { LastChildFill = true };
        var left = Perforations();
        var right = Perforations();
        DockPanel.SetDock(left, Dock.Left); DockPanel.SetDock(right, Dock.Right);
        strip.Children.Add(left); strip.Children.Add(right); strip.Children.Add(frames);
        return d.Stage(Box(strip, bg, 19, 8), C("#94634D"), Colors.White, w);
    }

    private static FrameworkElement Telegram(Studio d)
    {
        var tele = C("#E9D9B4");
        var rust = C("#A24C39");
        var w = Math.Min(d.Width, d.Wide ? 1230 : 880);
        var head = Pair(d.TitleText("TELEGRAM", Ink, 66, DmSerif), d.Small("MESSAGE OFFICE\n" + d.Text("date"), rust, 25, TextAlignment.Right, Mono), .58);
        var sheet = Stack(head, Space(Rule(rust, weight: 5), 15, 24),
            Pair(d.Small("FROM  " + d.Text("from"), Ink, 27, font: Mono), d.Small("TO  " + d.Text("to"), Ink, 27, font: Mono)),
            Space(Rule(C("#A48769"), dotted: true), 20, 20),
            d.Lyrics(w - 112, Ink, 65, face: TerminalFace),
            Space(Rule(C("#A48769"), dotted: true), 24, 16),
            Pair(d.Small(d.Text("note"), Ink, 25, font: Mono), Stamp(d.Text("stamp"), rust), .55));
        return d.Stage(Box(sheet, tele, 55, 4), C("#493B34"), Colors.White, w);
    }

    private static FrameworkElement Museum(Studio d)
    {
        var wall = C("#E8E4DB");
        var bronze = C("#7D634A");
        var w = Math.Min(d.Width, d.Wide ? 1180 : 890);
        var header = Stack(d.Small(d.Text("collection").ToUpperInvariant(), bronze, 26),
            Space(d.TitleText(d.Title, Ink, 69, DmSerif), 7), Space(d.Small(string.Join("  ·  ", new[] { d.Artist, d.Text("year") }.Where(s => !string.IsNullOrWhiteSpace(s))), bronze, 26), 7));
        var plaque = Box(Stack(header, Space(Rule(bronze), 18, 18), d.Lyrics(w - 96, Ink, 62),
            Space(d.Small(d.Text("id") + "\n" + d.Text("note"), bronze, 22), 22)), C("#FAF7EE"), 45, 0, C("#C5B8A0"));
        FrameworkElement gallery = plaque;
        if (d.Photo(d.Art(450)) is { } picture)
        {
            var frame = Box(Box(picture, C("#FDFBF4"), 25), C("#493A29"), 14);
            gallery = Stack(Space(frame, 0, d.H(40)), plaque);
        }
        return d.Stage(gallery, wall, Ink, w, new LinearGradientBrush(C("#F1EEE5"), C("#B9AE9C"), 94));
    }

    private static FrameworkElement Messenger(Studio d)
    {
        var slate = C("#205166");
        var pale = C("#DDF0F2");
        var accent = C("#146C6B");
        var w = Math.Min(d.Width, d.Wide ? 1160 : 880);
        var name = Stack(d.TitleText(d.Text("who"), Ink, 48), d.Small("●  " + d.Text("status"), accent, 26));
        FrameworkElement person = name;
        if (d.Photo(d.Art(148), 12) is { } avatar)
            person = Pair(name, avatar, .77, 30);
        var message = Box(Stack(d.Small(d.Text("who") + "  ·  " + d.Text("time"), accent, 25),
            Space(d.Lyrics(w - 146, Ink, 64), 15)), Colors.White, 28, 22);
        var screen = Stack(person, Space(message, 28),
            Space(Box(d.Small(d.Text("note") + "   ▏", slate, 28), C("#F3FBFA"), 20, 11, C("#9CC5CC")), 23),
            Space(d.Small("♫  " + d.Title, accent, 24), 19));
        var chrome = Box(d.Small("Conversation  /  Messenger                           ◇   –   ×", Colors.White, 28), slate, 18, 9);
        return d.Stage(Box(Stack(chrome, Box(screen, pale, 32, 12)), C("#9BC6CE"), 12, 18), C("#263C54"), Colors.White, w);
    }

    private static FrameworkElement ErrorDialog(Studio d)
    {
        var sky = C("#273D9B");
        var magenta = C("#9F355F");
        var body = C("#F6EDD9");
        var w = Math.Min(d.Width, d.Wide ? 1120 : 875);
        var icon = d.Text("icon") switch { "Warning" => "!", "Info" => "i", _ => "×" };
        var badge = Box(d.TitleText(icon, Colors.White, 83, align: TextAlignment.Center), magenta, 10, 56);
        badge.Width = badge.Height = d.H(122);
        var meta = Stack(d.TitleText(d.Text("message"), Ink, 56), d.Small(d.Text("code"), magenta, 27, font: Mono));
        var buttons = Keys([d.Text("primary"), d.Text("secondary")], 2, d.H(74), C("#DDD9C9"), Ink);
        var panel = Stack(Box(d.Small(d.Text("title") + "                         ◇  ×", Colors.White, 30), sky, 24, 5),
            Box(Stack(Pair(badge, meta, .24), Space(d.Lyrics(w - 112, Ink, 66), 33), Space(d.Song(C("#6D594F"), 24), 12), Space(buttons, 37)), body, 47, 6));
        return d.Stage(Box(panel, C("#AFC1E3"), 8, 12), C("#667DA5"), Colors.White, w,
            new LinearGradientBrush(C("#26365E"), C("#718183"), 58));
    }

    private static FrameworkElement Floppy(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 960 : 880);
        var plastic = d.Color("device", Darken(d.Accent, .45));
        var label = C("#E8D8C7");
        var metal = C("#C1C7CE");
        var shutter = new Grid { Height = d.H(184), Width = w * .53, HorizontalAlignment = HorizontalAlignment.Center, Background = Brush(metal) };
        shutter.Children.Add(new Border { Width = w * .15, Margin = new Thickness(0, 0, 30, 12), HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brush(C("#252B33")), CornerRadius = new CornerRadius(0, 0, 12, 12) });
        var paper = Box(Stack(d.Small(d.Text("label"), C("#754B47"), 27), d.TitleText(d.Text("file"), Ink, 47, Mono),
            Space(Rule(C("#AA7A6D"), weight: 5), 18, 18),
            d.Lyrics(w - 158, Ink, 68, face: TerminalFace), Space(d.Small(d.Artist, Ink, 25, font: Mono), 18)), label, 34, 8);
        var caseBody = Stack(shutter, Space(paper, d.H(70)), Space(Pair(d.Small(d.Text("id"), Colors.White, 28, font: Mono),
            d.Small("◢   ◇", Colors.White, 30, TextAlignment.Right)), d.H(45)));
        return d.Stage(Box(caseBody, plastic, 40, 25, Lighten(plastic, .15)), C("#302C35"), Colors.White, w);
    }

    private static FrameworkElement Terminal(Studio d)
    {
        var (back, fg) = d.Text("theme") switch
        {
            "Amber" => (C("#21190B"), C("#F9C06B")),
            "Mono" => (C("#1F2326"), C("#F4F4F2")),
            "Blue" => (C("#101C36"), C("#B3DEFF")),
            "Modern" => (C("#1E162B"), C("#DDC7F4")),
            _ => (C("#121A16"), C("#BCE89E")),
        };
        var w = Math.Min(d.Width, d.Wide ? 1350 : 910);
        var prompt = d.Text("user") + "@" + d.Text("host") + ":~$ ";
        var page = Stack(d.Small(prompt + d.Text("command"), fg, 29, font: Mono), d.Small("TRACK    " + d.Title + "\nARTIST   " + d.Artist, Lighten(fg, .35), 25, font: Mono),
            Space(d.Small("$ " + d.Text("lyrics"), fg, 30, font: Mono), 30, 18),
            d.Lyrics(w - 120, fg, 66, face: TerminalFace),
            Space(d.Small("▉  " + d.Text("note"), fg, 26, font: Mono), 25));
        var title = Pair(d.Small("●   ●   ●", fg, 22), d.Small("shell  /  lyrics", fg, 25, TextAlignment.Right, Mono));
        var terminal = Stack(Box(title, Lighten(back, .13), 18, 7), Box(page, back, 54, 7));
        return d.Stage(Box(terminal, back, 4, 14, Lighten(back, .2)), C("#242631"), Colors.White, w);
    }

    private static FrameworkElement BlueScreen(Studio d)
    {
        var blue = C("#152F9E");
        var fg = C("#FFF9E7");
        var w = Math.Min(d.Width, 1220);
        var tag = Box(d.Small("    TASKITIFY  /  FEELINGS    ", blue, 27, TextAlignment.Center, Mono), fg, 7);
        tag.HorizontalAlignment = HorizontalAlignment.Center;
        var content = Stack(tag, Space(d.TitleText(d.Text("intro"), fg, 55, Mono), d.H(56)),
            d.Small(d.Text("code"), C("#BDE8FF"), 34, font: Mono), Space(d.Lyrics(w - 60, fg, 70, face: TerminalFace), d.H(44)),
            Space(d.Small("TECHNICAL INFORMATION\n" + d.Title + "  /  " + d.Artist, fg, 24, font: Mono), d.H(45)),
            Space(d.Small(d.Text("note") + "  ▏", fg, 25, font: Mono), 19));
        return d.Stage(content, blue, fg, w);
    }

    private static FrameworkElement Away(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1110 : 845);
        var purple = C("#5B416D");
        var gold = C("#B4735C");
        var body = C("#FEF1D3");
        var header = Stack(d.Small(d.Text("user"), purple, 40), d.Small(d.Text("status") + "   /   " + d.Text("time"), gold, 27, font: Mono));
        var profile = d.Photo(d.Art(120), 3) is { } avatar ? Pair(avatar, header, .2) : header;
        var note = Stack(profile, Space(d.Small(d.Text("intro"), Ink, 30), d.H(30)),
            Space(Box(d.Lyrics(w - 136, Ink, 64), C("#FFFFFF"), 27, 3, C("#D4B29D")), 22),
            Space(d.Small(d.Text("note"), purple, 27), 23), Space(d.Song(gold, 26), 24));
        var frame = Stack(Box(d.Small("✺    Away Message                              _   ×", Colors.White, 32), purple, 19, 4),
            Box(note, body, 38, 4), Box(d.Small("✓  MESSAGE STORED LOCALLY", purple, 23, font: Mono), C("#E8D2AB"), 15, 2));
        return d.Stage(Box(frame, C("#D8BBA2"), 5, 8), C("#DAB394"), Ink, w, LinedPaper(C("#DAB394"), C("#CBA588"), 33, grid: true));
    }

    private static FrameworkElement Calculator(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1160 : 815);
        var body = d.Color("device", C("#373D44"));
        var fg = TextOn(body);
        var lcd = C("#C8D5AA");
        var display = Box(Stack(d.Small(d.Text("expression"), Ink, 28, font: Mono),
            Space(d.Lyrics(w - 164, Ink, 64, TextAlignment.Right, TerminalFace), 18),
            Space(d.Small(d.Text("result"), C("#46553C"), 31, TextAlignment.Right, Mono), 17)), lcd, 35, 16, C("#859078"));
        var keys = Keys(["sin","cos","tan","MODE","AC","7","8","9","÷","DEL","4","5","6","×","−","1","2","3","+","="], 5, d.H(58), Lighten(body, .19), fg, Mono);
        return d.Stage(Box(Stack(Pair(d.Small("TKT / SCIENTIFIC", fg, 26, font: Mono), d.Small("FX–27", fg, 26, TextAlignment.Right, Mono)),
            Space(display, 27), Space(keys, d.H(28)), Space(d.Song(fg, 25), 23)), body, 43, 34, Lighten(body, .23)),
            C("#BCC3AE"), Ink, w);
    }

    private static FrameworkElement Paint(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1260 : 900);
        var chrome = C("#DFD7CD");
        var blue = C("#423D75");
        var doodle = d.Color("doodle", C("#CF4E72"));
        var paintInk = d.Color("ink", C("#31233F"));
        var doodles = d.Text("shape") switch { "Hearts" => "♡       ♥     ♡", "Stars" => "✦      ✧      ✶", _ => "" };
        var sketch = Stack(d.Lyrics(w - 210, paintInk, 66), Space(d.Small(doodles, doodle, 45, TextAlignment.Right), 14));
        if (d.Text("shape") == "Underline") sketch.Children.Add(Space(Rule(doodle, weight: 6), 13));
        var tools = Keys(["↖","□","○","╱","A","✦","◇","✎"], 2, d.H(47), C("#C8BACB"), blue);
        var screen = Pair(tools, Box(sketch, Colors.White, 29, 2, C("#A49BB2")), .14, 15);
        var swatches = new UniformGrid { Rows = 1, Height = 29 };
        foreach (var color in new[] { "#24202A", "#E4DFE8", "#944E62", "#DD764D", "#E7CA6B", "#77A48C", "#5981AA", "#7A6D9D", "#CF91AA" })
            swatches.Children.Add(new Border { Background = Brush(C(color)), Margin = new Thickness(2) });
        var panel = Stack(Box(d.Small(d.Text("file") + "  /  Paint Studio                 ◇  ×", Colors.White, 27), blue, 15),
            Space(d.Small("File    Edit    View    Image    Palette", blue, 24), 9, 12), screen,
            Space(swatches, 12), Space(d.Song(blue, 24), 12));
        return d.Stage(Box(panel, chrome, 13, 5, C("#9D90A7")), C("#8A7796"), Colors.White, w);
    }

    private static FrameworkElement Profile(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1310 : 895);
        var sky = C("#D9EBFA");
        var pink = C("#A64D81");
        var ink = C("#344967");
        var summary = Stack(d.TitleText(d.Text("profile"), ink, 48), d.Small("profile / friends / music", pink, 24));
        var identity = d.Photo(d.Art(140), 13) is { } avatar ? Pair(avatar, summary, .22, 22) : summary;
        var post = Box(Stack(d.Small(d.Text("section").ToUpperInvariant(), pink, 26),
            Space(d.Small(d.Text("from") + "  ·  " + d.Text("date"), ink, 25), 14),
            Space(d.Lyrics(w - 158, ink, 65), 20), Space(d.Song(pink), 20)), Colors.White, 35, 21);
        var account = Stack(Box(identity, sky, 29, 22), Space(post, 24),
            Space(d.Small("♡  memorable     ♫  on repeat", pink, 25), 21));
        return d.Stage(Box(account, C("#EEF4FF"), 36, 30, C("#AFBED6")), C("#ACBCD3"), Ink, w);
    }

    private static FrameworkElement PocketPlayer(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1070 : 800);
        var metal = d.Color("device", C("#E4DDCE"));
        var screen = C("#181F2D");
        var cyan = C("#91D8CB");
        var face = TextOn(metal);
        var info = Stack(d.Small(d.Text("label").ToUpperInvariant(), cyan, 24), d.TitleText(d.Title, Colors.White, 44), d.Small(d.Artist, C("#B5C0CD"), 27));
        var disk = new Grid { Width = d.H(235), Height = d.H(235), HorizontalAlignment = HorizontalAlignment.Center };
        disk.Children.Add(new Ellipse { Fill = Brush(Lighten(metal, .2)), Stroke = Brush(Darken(metal, .62)), StrokeThickness = 2 });
        disk.Children.Add(new Ellipse { Fill = Brush(metal), Width = d.H(107), Height = d.H(107), Stroke = Brush(Darken(metal, .68)), StrokeThickness = 2 });
        disk.Children.Add(new TextBlock { Text = "▶", FontSize = 30, Foreground = Brush(face), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        disk.Children.Add(new TextBlock { Text = "SELECT", FontSize = 20, Foreground = Brush(face), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 19, 0, 0) });
        var song = d.Photo(d.Art(162), 6) is { } thumb ? Pair(thumb, info, .27) : info;
        var panel = Box(Stack(song, Space(d.Progress(cyan, C("#4B5970"), 5), 21, 17), d.Lyrics(w - 145, Colors.White, 64),
            Space(d.Small((d.Timestamp ? d.Time + "  /  " : "") + d.Duration, cyan, 24, TextAlignment.Right, Mono), 16)), screen, 34, 24);
        return d.Stage(Box(Stack(panel, Space(disk, d.H(31)), Space(d.Small(d.Text("engrave"), face, 24, TextAlignment.Center), 20)),
            metal, 35, 47, Darken(metal, .65)), C("#72818D"), Colors.White, w);
    }
}
