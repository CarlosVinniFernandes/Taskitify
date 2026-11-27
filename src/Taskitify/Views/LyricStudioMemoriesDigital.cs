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
        var paper = C("#F4F0E7");
        var marks = C("#6A6150");
        var ground = Darken(d.Accent, .57);
        var w = d.Wide ? 1260 : 936;
        var copy = Stack(d.TitleText(d.Text("title"), Ink, 50),
            Space(d.Lyrics(d.Wide && d.Image is not null ? w * .49 - 82 : w - 112, Ink, 70), 23),
            Space(d.Song(marks, 27), 25));
        FrameworkElement content = copy;
        if (d.Photo(d.Art(d.Story ? 620 : 440), 0) is { } photo)
        {
            var exposure = Box(photo, C("#22201D"), 8);
            content = d.Wide ? Pair(exposure, copy, .51, 34) : Stack(exposure, Space(copy, 35));
        }
        var register = Pair(d.Small("▯  ▯  ▯     CONTACT PRINT", marks, 23, font: Mono),
            d.Small(d.Text("ref"), marks, 23, TextAlignment.Right, Mono), .46);
        var print = Box(Stack(register, Space(Rule(C("#B6AA95")), 12, 18), content,
            Space(Rule(C("#B6AA95"), dotted: true), 33)), paper, 49, 1);
        print.Background = Fibers(paper, Alpha(marks, 23), .8);
        return d.Stage(Mounted(print, 26), ground, Colors.White, w,
            Desk(Lighten(ground, .34), Darken(ground, .46)), inset: 60, footerGap: 31);
    }


    private static FrameworkElement Letter(Studio d)
    {
        var w = d.Wide ? 1250 : 964;
        var red = C("#875B56");
        var blue = C("#303C52");
        var top = Pair(d.Small("TO  /  " + d.Text("to"), red, 28),
            d.Small(d.Text("date"), red, 29, TextAlignment.Right, DmSerif), .58);
        var goodbye = Stack(d.Small(d.Text("bye"), blue, 34, TextAlignment.Right, DmSerif),
            d.TitleText(d.Text("sign"), red, 58, DmSerif, TextAlignment.Right));
        var page = Stack(top, Space(d.TitleText(d.Text("hello"), blue, 68, DmSerif), d.H(118)),
            Space(d.Lyrics(w - 182, blue, 76), 37), Space(goodbye, d.H(82)),
            Space(Rule(Alpha(red, 95)), d.H(38), 17), d.Song(red, 25));
        var sheet = Box(page, Paper, 78, 3);
        sheet.Background = LinedPaper(Paper, C("#D6CBD0"), d.H(71));
        sheet.BorderBrush = Brush(C("#AF726F"));
        sheet.BorderThickness = new Thickness(6, 0, 0, 0);
        return d.Stage(Mounted(sheet), C("#4D5659"), Colors.White, w,
            Desk(C("#A18F81"), C("#3C494E")), inset: 94, footerGap: 40);
    }


    private static FrameworkElement FilmNegative(Studio d)
    {
        var stage = C("#201815");
        var film = C("#141311");
        var cream = C("#F5D8AE");
        var amber = C("#D29765");
        var w = d.Wide ? 1430 : 1000;
        const double track = 57;
        var width = w - 2 * track - 51;
        Border Sprockets()
        {
            var tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(Brush(film), null, new RectangleGeometry(new Rect(0, 0, 57, 73))));
            tile.Children.Add(new GeometryDrawing(Brush(C("#81624A")), null, new RectangleGeometry(new Rect(14, 13, 29, 31), 5, 5)));
            tile.Children.Add(new GeometryDrawing(Brush(C("#523C30")), null, new RectangleGeometry(new Rect(14, 41, 29, 3))));
            return new Border { Width = track, Background = new DrawingBrush(tile) { TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 57, 73), ViewportUnits = BrushMappingMode.Absolute } };
        }
        var lyricFrame = Box(Stack(d.Small("▸  " + d.Text("frame") + "    /    SOUND PRINT", amber, 24, font: Mono),
            Space(d.Lyrics(d.Wide && d.Image is not null ? width * .52 - 74 : width - 76, cream, 73), 21),
            Space(d.Song(amber, 25), 22)), C("#30221A"), 34, 0, C("#78523D"));
        FrameworkElement frames = lyricFrame;
        if (d.Photo(d.Art(d.Story ? 630 : 510)) is { } photo)
        {
            var photoFrame = Box(photo, film, 6, 0, C("#98704C"));
            frames = d.Wide ? Pair(photoFrame, lyricFrame, .48, 18) : Stack(photoFrame, Space(lyricFrame, 15));
        }
        var metadata = Pair(d.Small(d.Text("stock"), amber, 24, font: Mono), d.Small(d.Text("note"), amber, 23, TextAlignment.Right, Mono));
        var core = Stack(metadata, Space(frames, 22), Space(d.Small("▴   " + d.Text("frame"), amber, 26, TextAlignment.Right, Mono), 20));
        var strip = new DockPanel();
        var left = Sprockets();
        var right = Sprockets();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        strip.Children.Add(left);
        strip.Children.Add(right);
        strip.Children.Add(Box(core, film, 14));
        return d.Stage(Mounted(Box(strip, film, 7, 2, C("#543A2C")), 19), stage, cream, w,
            Desk(C("#6B4636"), stage), inset: 36, footerGap: 23);
    }


    private static FrameworkElement Telegram(Studio d)
    {
        var paper = C("#ECDDAD");
        var vermilion = C("#963D2C");
        var w = d.Wide ? 1332 : 980;
        var head = Pair(d.TitleText("TELEGRAM", Ink, d.Story ? 86 : 78, DmSerif),
            d.Small("MESSAGE OFFICE\n" + d.Text("date"), vermilion, 29, TextAlignment.Right, Mono), .63);
        var fields = Pair(Stack(d.Small("FROM", vermilion, 23, font: Mono), d.Small(d.Text("from"), Ink, 32, font: Mono)),
            Stack(d.Small("TO", vermilion, 23, font: Mono), d.Small(d.Text("to"), Ink, 32, font: Mono)), .45, 34);
        var contents = Stack(head, Space(Rule(vermilion, weight: 7), 14, 34), fields,
            Space(Rule(C("#957959"), dotted: true), 23, 26),
            d.Lyrics(w - 169, Ink, 75, face: TerminalFace),
            Space(Rule(C("#957959"), dotted: true), d.H(47), 24),
            Pair(d.Small(d.Text("note"), Ink, 28, font: Mono), Stamp(d.Text("stamp"), vermilion, 29), .49));
        var sheet = Box(contents, paper, 70, 2);
        sheet.Background = Fibers(paper, Alpha(C("#957959"), 54), 1.1);
        sheet.BorderThickness = new Thickness(10, 1, 1, 1);
        sheet.BorderBrush = Brush(vermilion);
        return d.Stage(Mounted(sheet, 25), C("#393833"), Colors.White, w, Desk(C("#7C6757"), C("#34332D")), inset: 56);
    }


    private static FrameworkElement Museum(Studio d)
    {
        var wall = C("#D9D6CA");
        var bronze = C("#635743");
        var w = d.Wide ? 1430 : 942;
        var picture = d.Photo(d.Art(d.Story ? 620 : 525));
        var labelWidth = d.Wide && picture is not null ? w * .47 - 85 : w - 106;
        var header = Stack(d.Small(d.Text("collection").ToUpperInvariant(), bronze, 25, font: Mono),
            Space(d.TitleText(d.Title, Ink, 71, DmSerif), 9),
            d.Small(string.Join("  ·  ", new[] { d.Artist, d.Text("year") }.Where(value => !string.IsNullOrWhiteSpace(value))), bronze, 30));
        var plaque = Box(Stack(header, Space(Rule(C("#A89B81")), 21, 22), d.Lyrics(labelWidth, Ink, 68),
            Space(d.Small(d.Text("id") + "\n" + d.Text("note"), bronze, 24), 29)), C("#F9F5E9"), 43, 0, C("#B9B09C"));
        FrameworkElement gallery = plaque;
        if (picture is not null)
        {
            var frame = Mounted(Enamel(Box(picture, C("#F6EEDD"), 31), C("#514339"), new Thickness(16),
                new CornerRadius(2)), 24);
            gallery = d.Wide ? Pair(frame, plaque, .53, 42) : Stack(frame, Space(plaque, d.H(57)));
        }
        return d.Stage(gallery, wall, Ink, w, Desk(C("#F5EFE2"), C("#9A9789")), inset: 56, footerGap: 32);
    }


    private static FrameworkElement Messenger(Studio d)
    {
        var slate = C("#174F66");
        var aqua = C("#20696B");
        var light = C("#E3F1ED");
        var w = d.Wide ? 1365 : 978;
        var sidebar = Stack(d.Small("CONTACT", slate, 22, font: Mono));
        if (d.Photo(d.Art(d.Story ? 235 : 185), 6) is { } avatar)
            sidebar.Children.Add(Space(Box(avatar, Colors.White, 6, 7, C("#96BCB3")), 11));
        sidebar.Children.Add(Space(d.TitleText(d.Text("who"), Ink, 35), 14));
        sidebar.Children.Add(d.Small("●  " + d.Text("status"), aqua, 23));
        sidebar.Children.Add(Space(d.Small("♫  On repeat", slate, 23), 33));
        sidebar.Children.Add(d.Small(d.Title, slate, 24));
        var message = Box(Stack(Pair(d.Small(d.Text("who"), aqua, 25),
            d.Small(d.Text("time"), aqua, 24, TextAlignment.Right, Mono), .70), Space(Rule(C("#C2DBD5")), 12, 18),
            d.Lyrics((w - 128) * .72 - 56, Ink, 67)), Colors.White, 25, 12, C("#AFCBC6"));
        var tools = d.Small("Aa       ☺       ♪       +", slate, 28);
        var typing = Box(Stack(tools, Space(Rule(C("#C2DBD5")), 9, 13), d.Small(d.Text("note") + "  ▏", slate, 25)), C("#F9FCF7"), 19, 7, C("#AFCBC6"));
        var chat = Stack(message, Space(typing, 20));
        var workspace = Pair(Box(sidebar, C("#D1E7DF"), 15, 7), chat, .28, 17);
        var menus = Box(d.Small("Conversation     Contacts     Tools     Help", slate, 24), C("#C2DEDA"), 13);
        var panel = Stack(WindowBar(d, "Messenger  /  " + d.Text("who"), Colors.White, slate, C("#338884"), ["–", "□", "×"]),
            menus, Box(workspace, light, 26), Box(d.Small("●  " + d.Text("status"), slate, 22), C("#BDD6D0"), 12));
        return d.Stage(Mounted(Box(panel, slate, 5, 12), 26), C("#233D4B"), Colors.White, w,
            Desk(C("#6F9F9D"), C("#233D4B")), inset: 90);
    }


    private static FrameworkElement ErrorDialog(Studio d)
    {
        var night = C("#354879");
        var chrome = C("#313C89");
        var paper = C("#F8F0E3");
        var w = d.Wide ? 1210 : 952;
        var (mark, color) = d.Text("icon") switch { "Warning" => ("!", C("#AF632B")), "Info" => ("i", C("#43868D")), _ => ("×", C("#AE3854")) };
        var badge = Enamel(d.TitleText(mark, Colors.White, 86, align: TextAlignment.Center), color, new Thickness(5), new CornerRadius(63));
        badge.Width = d.H(118);
        badge.Height = d.H(118);
        var meta = Stack(d.TitleText(d.Text("message"), Ink, 59), Space(d.Small(d.Text("code"), color, 30, font: Mono), 11));
        var body = Stack(Pair(badge, meta, .23, 25), Space(Rule(C("#D8C9BD")), 32, 28),
            d.Lyrics(w - 144, Ink, 72), Space(d.Song(C("#736562"), 25), 28));
        var keys = Keys([d.Text("primary"), d.Text("secondary")], 2, d.H(79), C("#E1DBDC"), Ink);
        var dialog = Stack(WindowBar(d, d.Text("title"), Colors.White, chrome, C("#6D76AC"), ["?", "×"]),
            Box(body, paper, 53), Box(keys, C("#D8D0D2"), 19, 5));
        return d.Stage(Mounted(Box(dialog, C("#BFC7E0"), 7, 11), 28), night, Colors.White, w,
            Desk(C("#8697C5"), C("#293753")), inset: 67);
    }


    private static FrameworkElement Floppy(Studio d)
    {
        var w = d.Wide ? 1110 : 952;
        var plastic = d.Color("device", Darken(d.Accent, .54));
        var silver = C("#B6C5C8");
        var ivory = C("#EFE3CF");
        var stripe = C("#8F3F46");
        var hardware = new Grid { Height = d.H(231) };
        var shutter = Recess(new Grid(), silver, 0, 8);
        shutter.Width = w * .52;
        shutter.HorizontalAlignment = HorizontalAlignment.Right;
        shutter.Margin = new Thickness(0, -29, 38, 0);
        hardware.Children.Add(shutter);
        var slot = Recess(new Grid(), C("#29292C"), 0, 8);
        slot.Width = w * .135; slot.Height = d.H(157);
        slot.HorizontalAlignment = HorizontalAlignment.Right;
        slot.VerticalAlignment = VerticalAlignment.Top;
        slot.Margin = new Thickness(0, -7, w * .17, 0);
        hardware.Children.Add(slot);
        var spec = d.Small(d.Text("id"), Lighten(plastic, .67), 29, font: Mono);
        spec.VerticalAlignment = VerticalAlignment.Bottom;
        hardware.Children.Add(spec);
        var label = Box(Stack(Box(d.Small(d.Text("label").ToUpperInvariant(), ivory, 27, font: Mono), stripe, 17),
            Space(d.TitleText(d.Text("file"), Ink, 49, Mono), 24), Space(Rule(C("#A79280")), 16, 17),
            d.Lyrics(w - 154, Ink, 72, face: TerminalFace), Space(d.Small(d.Artist, stripe, 25, font: Mono), 23)), ivory, 27, 5);
        label.Background = LinedPaper(ivory, C("#E0D0BC"), d.H(55));
        var bottom = Pair(Screw(silver, 34), Vents(Darken(plastic, .51), 4, 12, d.H(41)), .5);
        var disk = Enamel(Stack(hardware, Space(label, d.H(49)), Space(bottom, d.H(45))),
            plastic, new Thickness(42, 29, 40, 34), new CornerRadius(54, 14, 21, 21));
        return d.Stage(Mounted(disk, 30), C("#2B272F"), Colors.White, w, Desk(C("#665A63"), C("#24242D")), inset: 82);
    }


    private static FrameworkElement Terminal(Studio d)
    {
        var (back, fg) = d.Text("theme") switch
        {
            "Amber" => (C("#261A0D"), C("#F7C581")),
            "Mono" => (C("#202327"), C("#F0F0EB")),
            "Blue" => (C("#111E38"), C("#B8E0FC")),
            "Modern" => (C("#241A31"), C("#E6CDEC")),
            _ => (C("#122017"), C("#C4E9AA")),
        };
        var w = d.Wide ? 1530 : 1004;
        var dim = Alpha(fg, 155);
        var prompt = d.Text("user") + "@" + d.Text("host") + ":~$ ";
        var display = Stack(d.Small(prompt + d.Text("command"), fg, 30, font: Mono),
            Space(d.Small("TRACK    " + d.Title + "\nARTIST   " + d.Artist, dim, 29, font: Mono), 24),
            Space(d.Small("$ " + d.Text("lyrics"), dim, 33, font: Mono), d.H(79), 25),
            d.Lyrics(w - 172, fg, 76, face: TerminalFace),
            Space(d.Small("▉  " + d.Text("note"), dim, 29, font: Mono), d.H(58)));
        var console = Box(display, back, 62);
        console.Background = LinedPaper(back, Lighten(back, .04), 10);
        var chrome = WindowBar(d, "shell  /  " + d.Text("host"), fg, Lighten(back, .16), Lighten(back, .08), ["–", "□", "×"]);
        var bottom = Box(Pair(d.Small("UTF-8", dim, 23, font: Mono), d.Small("LYRIC STREAM   ▪", dim, 23, TextAlignment.Right, Mono)), Lighten(back, .10), 16);
        return d.Stage(Mounted(Box(Stack(chrome, console, bottom), Lighten(back, .17), 4, 13), 17), back, fg, w,
            Desk(Lighten(back, .26), Darken(back, .43)), inset: 46, footerGap: 26);
    }


    private static FrameworkElement BlueScreen(Studio d)
    {
        var blue = C("#17349C");
        var white = C("#FFF9E9");
        var ice = C("#ABE5F6");
        var w = d.Wide ? 1450 : 937;
        var signal = Pair(d.Small("▰ ▰ ▰    RECOVERY CONSOLE", white, 25, font: Mono),
            d.Small("00 : 2F", ice, 25, TextAlignment.Right, Mono), .83);
        var code = d.TitleText(d.Text("code"), white, d.Story ? 59 : 49, Mono);
        var tech = Box(Stack(d.Small("TECHNICAL INFORMATION", ice, 23, font: Mono),
            d.Small(d.Title + "  /  " + d.Artist, white, 26, font: Mono)), C("#253C84"), 25);
        var content = Stack(signal, Space(Rule(ice), 14, d.H(72)),
            d.Small(d.Text("intro"), ice, 30, font: Mono), Space(code, 18),
            Space(d.Lyrics(w - 38, white, 77, face: TerminalFace), d.H(66)),
            Space(tech, d.H(62)), Space(d.Small(d.Text("note") + "   ▉", ice, 27, font: Mono), 29));
        return d.Stage(content, blue, white, w, LinedPaper(blue, C("#1C389E"), 13), top: d.Story, inset: 154, footerGap: 58);
    }


    private static FrameworkElement Away(Studio d)
    {
        var w = d.Wide ? 1290 : 945;
        var plum = C("#4E3B67");
        var ochre = C("#A96848");
        var cream = C("#FBEDD2");
        var name = Stack(d.TitleText(d.Text("user"), plum, 43), Space(d.Small("◉  " + d.Text("status"), ochre, 26), 6));
        var portrait = d.Photo(d.Art(154), 4);
        FrameworkElement identity = portrait is null ? name : Pair(Box(portrait, Colors.White, 5), name, .20, 23);
        var note = Box(Stack(d.Small(d.Text("intro"), plum, 29),
            Space(d.Lyrics(w - 178, Ink, 72), 23),
            Space(d.Small(d.Text("note"), ochre, 29), 24)), Colors.White, 31, 3, C("#D5B99F"));
        var line = Pair(d.Small(d.Text("time"), plum, 25, font: Mono), d.Small("ONE-WAY MESSAGE", ochre, 23, TextAlignment.Right, Mono));
        var sheet = Stack(identity, Space(line, 24, 14), note, Space(d.Song(plum, 26), 22));
        var chrome = WindowBar(d, "✺    Away Message", Colors.White, plum, C("#876478"), ["–", "×"]);
        var window = Stack(chrome, Box(sheet, cream, 46), Box(d.Small("✓   Available when the music ends", plum, 23), C("#E6CDB0"), 16));
        return d.Stage(Mounted(Box(window, C("#BFA689"), 5, 8), 27), C("#A9816F"), Ink, w,
            LinedPaper(C("#DAB493"), C("#D0A487"), 49, grid: true), inset: 74);
    }


    private static FrameworkElement Calculator(Studio d)
    {
        var w = d.Wide ? 1115 : 863;
        var caseColor = d.Color("device", C("#383E49"));
        var marks = TextOn(caseColor);
        var lcd = C("#BDCC9C");
        var shadow = C("#3B4E31");
        var display = Recess(Stack(d.Small(d.Text("expression"), shadow, 28, font: Mono),
            Space(d.Lyrics(w - 190, shadow, 69, TextAlignment.Right, TerminalFace), 26),
            Space(d.Small(d.Text("result"), shadow, 32, TextAlignment.Right, Mono), 23)), lcd, 34, 11);
        var top = Pair(d.Small("TKT  /  SCIENTIFIC", marks, 28, font: Mono),
            d.Small("FX–27", marks, 25, TextAlignment.Right, Mono), .72);
        var solar = new UniformGrid { Columns = 9, Height = d.H(37), Width = 234, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < 9; i++)
            solar.Children.Add(new Border { Background = Brush(i % 2 == 0 ? C("#594137") : C("#43322E")), Margin = new Thickness(1) });
        var functions = Keys(["2nd","MODE","sin","cos","tan"], 5, d.H(51), C("#597084"), Colors.White, Mono);
        var keypad = Keys(["7","8","9","DEL","AC","4","5","6","×","÷","1","2","3","+","−","0",".","EXP","ANS","="],
            5, d.H(73), Lighten(caseColor, .16), marks, Mono);
        var shell = Enamel(Stack(top, Space(solar, 14, 14), display, Space(functions, d.H(32)),
            Space(keypad, 4), Space(d.Song(marks, 23), 23)), caseColor, new Thickness(41, 38, 41, 38), new CornerRadius(42, 42, 65, 65));
        return d.Stage(Mounted(shell, 31), C("#636F5D"), Colors.White, w, Desk(C("#C7CBAC"), C("#535E52")), inset: 65);
    }


    private static FrameworkElement Paint(Studio d)
    {
        var w = d.Wide ? 1420 : 976;
        var metal = C("#DFD7CD");
        var purple = C("#423D75");
        var doodle = d.Color("doodle", C("#CF4E72"));
        var inkColor = d.Color("ink", C("#31233F"));
        var doodles = d.Text("shape") switch { "Hearts" => "♡       ♥     ♡", "Stars" => "✦      ✧      ✶", _ => "" };
        var sketch = Stack(d.Lyrics((w - 73) * .84 - 95, inkColor, 72),
            Space(d.Small(doodles, doodle, 63, TextAlignment.Right), d.H(27)));
        if (d.Text("shape") == "Underline")
            sketch.Children.Add(Space(Rule(doodle, weight: 7), 17));
        var paper = Box(sketch, Colors.White, 38, 0, C("#A49BB2"));
        if (d.Story) paper.MinHeight = 590;
        var tools = Keys(["↖","⌖","▱","○","╱","▥","A","✦","◇","✎","●","↕"], 2, d.H(53), C("#C8BACB"), purple);
        var workArea = Pair(Box(tools, metal, 5), Box(paper, C("#B3A7B7"), 13), .16, 12);
        var swatches = new UniformGrid { Rows = 1, Height = 33 };
        foreach (var color in new[] { "#24202A", "#E4DFE8", "#944E62", "#DD764D", "#E7CA6B", "#77A48C", "#5981AA", "#7A6D9D", "#CF91AA" })
            swatches.Children.Add(new Border { Background = Brush(C(color)), Margin = new Thickness(2), BorderThickness = new Thickness(1), BorderBrush = Brush(C("#9A8FA1")) });
        var menus = Box(d.Small("File    Edit    View    Image    Palette", purple, 25), C("#E9E0DB"), 13);
        var panel = Stack(WindowBar(d, d.Text("file") + "  /  Paint Studio", Colors.White, purple, C("#6D5C98"), ["–", "□", "×"]),
            menus, Box(workArea, C("#C8BFCB"), 9), Box(swatches, metal, 13),
            Box(Pair(d.Song(purple, 24), d.Small("100%    ▥", purple, 22, TextAlignment.Right, Mono)), C("#E9E0DB"), 13));
        return d.Stage(Mounted(Box(panel, metal, 5, 7), 23), C("#72668C"), Colors.White, w,
            Desk(C("#9A88A9"), C("#514965")), inset: 48);
    }


    private static FrameworkElement Profile(Studio d)
    {
        var w = d.Wide ? 1430 : 1000;
        var ink = C("#334765");
        var rose = C("#A14679");
        var azure = C("#D8E7FA");
        var nav = C("#7392BE");
        var picture = d.Photo(d.Art(213), 4);
        var identity = Stack(d.Small("PROFILE", rose, 22), Space(d.TitleText(d.Text("profile"), ink, 37), 9));
        if (picture is not null)
            identity.Children.Insert(0, Space(Box(picture, Colors.White, 5, 6, C("#ADC2DE")), 0, 14));
        identity.Children.Add(Space(d.Small("Friends\nMusic\nAbout", rose, 26), 27));
        var side = Box(identity, azure, 19, 10, C("#ADBFDD"));
        var note = Box(Stack(d.Small("FROM   " + d.Text("from"), ink, 29), d.Small(d.Text("date"), rose, 24, font: Mono),
            Space(Rule(C("#C0CEE5")), 17, 19), d.Lyrics((w - 96) * .74 - 75, ink, 66),
            Space(d.Song(rose, 25), 23)), Colors.White, 34, 9, C("#AAC0D9"));
        var post = Stack(Box(d.TitleText(d.Text("section"), ink, 40), azure, 16, 9), Space(note, 13), Space(d.Small("♡  memorable       ♪  on repeat", rose, 24), 16));
        var panel = Stack(WindowBar(d, "·  social scrapbook", Colors.White, nav, C("#8780AD"), ["◇", "×"]),
            Box(d.Small("Home     Profile     Friends     Music", ink, 23), C("#C8DAEF"), 14),
            Box(Pair(side, post, .26, 19), C("#EAF1FC"), 27));
        return d.Stage(Mounted(Box(panel, C("#A8BBD8"), 6, 10), 27), C("#6B83AA"), Colors.White, w,
            Desk(C("#B4C7D9"), C("#586990")), inset: 72);
    }


    private static FrameworkElement PocketPlayer(Studio d)
    {
        var w = d.Wide ? 1120 : 853;
        var shell = d.Color("device", C("#DDD8CD"));
        var screen = C("#172330");
        var lcd = C("#D8EBDF");
        var aqua = C("#83CDC3");
        var ink = TextOn(shell);
        var song = Stack(d.Small(d.Text("label").ToUpperInvariant(), aqua, 23, font: Mono),
            d.TitleText(d.Title, Colors.White, 46), d.Small(d.Artist, lcd, 26));
        FrameworkElement meta = song;
        if (d.Photo(d.Art(d.Story ? 224 : 172), 6) is { } cover)
            meta = Pair(cover, song, .31, 25);
        var panel = Recess(Stack(meta, Space(d.Progress(aqua, C("#3C4B59"), 7), 23, 24),
            d.Lyrics(w - 173, lcd, 69), Space(d.Small((d.Timestamp ? d.Time + " / " : "") + d.Duration,
                aqua, 24, TextAlignment.Right, Mono), 24)), screen, 33, 24);
        var size = d.H(341);
        var wheel = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        wheel.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Lighten(shell, .44), Darken(shell, .79), 100),
            Stroke = Brush(Darken(shell, .52)), StrokeThickness = 3 });
        wheel.Children.Add(new Ellipse { Width = size * .43, Height = size * .43, Fill = Brush(shell),
            Stroke = Brush(Darken(shell, .61)), StrokeThickness = 2 });
        foreach (var (glyph, horizontal, vertical) in new[] { ("BROWSE", HorizontalAlignment.Center, VerticalAlignment.Top),
            ("◀◀", HorizontalAlignment.Left, VerticalAlignment.Center), ("▶▶", HorizontalAlignment.Right, VerticalAlignment.Center),
            ("▶ ▏▏", HorizontalAlignment.Center, VerticalAlignment.Bottom) })
        {
            var legend = d.Small(glyph, ink, 22);
            legend.HorizontalAlignment = horizontal; legend.VerticalAlignment = vertical;
            legend.Margin = new Thickness(size * .10);
            wheel.Children.Add(legend);
        }
        var player = Enamel(Stack(panel, Space(wheel, d.H(46)),
            Space(d.Small(d.Text("engrave"), ink, 23, TextAlignment.Center), 25)), shell,
            new Thickness(48, 48, 48, 40), new CornerRadius(60));
        return d.Stage(Mounted(player, 31), C("#3C5360"), Colors.White, w, Desk(C("#8FA3A6"), C("#304752")), inset: 60);
    }

}
