using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskitify.Views;

public static partial class LyricCardRenderer
{
    private static FrameworkElement Dpad(Color color, double size)
    {
        var plate = new Grid { Width = size, Height = size };
        plate.Children.Add(new Border { Height = size * .34, Background = Brush(color), CornerRadius = new CornerRadius(8) });
        plate.Children.Add(new Border { Width = size * .34, Background = Brush(color), CornerRadius = new CornerRadius(8) });
        plate.Children.Add(new Ellipse { Width = size * .18, Height = size * .18, Fill = Brush(Lighten(color, .22)) });
        return plate;
    }

    private static FrameworkElement PocketGame(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1050 : 825);
        var shell = d.Color("device", C("#D9D5C8"));
        var bodyInk = TextOn(shell);
        var screen = d.Color("screen", C("#C7D79D"));
        var screenInk = Luminance(screen) > .48 ? C("#223725") : Colors.White;
        var tint = C("#9E4D76");
        var lcd = Box(Stack(d.Small(d.Text("note"), screenInk, 26, font: Pixel), Space(d.Lyrics(w - 190, screenInk, 65, face: PixelFace), 19),
            Space(d.Song(screenInk, 25), 15)), screen, 34, 8);
        var frame = Box(Stack(d.Small("●  BATTERY", C("#ECAEBC"), 24, font: Pixel), Space(lcd, 15)), C("#33383B"), 22, 17);
        var controls = Pair(Dpad(C("#41414D"), d.H(116)), Keys(["B","A"], 2, d.H(81), tint, Colors.White, Pixel), .58, 55);
        return d.Stage(Box(Stack(frame, Space(d.TitleText(d.Text("label"), bodyInk, 36), 17), Space(controls, d.H(32)),
            Space(d.Small("SELECT          START      /      ⋰⋰⋰", bodyInk, 24, TextAlignment.Center, Pixel), 19)),
            shell, 37, 50, Darken(shell, .65)), C("#7B748E"), Colors.White, w);
    }

    private static FrameworkElement DualScreen(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1290 : 890);
        var enamel = d.Color("device", C("#9DA6C6"));
        var frame = C("#202738");
        var fg = C("#E4E7FD");
        var screen = C("#DCE6F1");
        var turquoise = C("#6DD8CF");
        var width = d.Wide ? (w - 146) / 2 : w - 139;
        var names = Stack(d.Small("NOW PLAYING", turquoise, 24, font: Pixel),
            d.TitleText(d.Title, fg, 42), d.Small(d.Artist, fg, 27),
            Space(d.Progress(turquoise, C("#47506E"), 5), 15),
            Space(d.Small((d.Timestamp ? d.Time + "  /  " : "") + d.Duration, fg, 23, font: Pixel), 12));
        var picture = d.Photo(d.Art(170), 6);
        var display = Box(picture is null ? names : Pair(picture, names, .31, 19), frame, 27, 17);
        var dialogue = Box(Stack(d.Small(d.Text("intro"), Ink, 28, font: Pixel), Space(d.Lyrics(width - 35, Ink, 64, face: PixelFace), 17),
            Space(d.Small("▼", Ink, 22, TextAlignment.Right, Pixel), 8)), screen, 27, 17, C("#38495C"));
        var hinge = new Border { Height = d.H(32), CornerRadius = new CornerRadius(14), Background = Brush(C("#56627A")) };
        var screens = d.Wide ? Pair(display, dialogue, .49, 31) : Stack(display, Space(hinge, 16, 16), dialogue);
        var control = Pair(Dpad(frame, d.H(78)), Keys(["Y","X","B","A"], 4, d.H(48), frame, fg, Pixel), .51, 20);
        return d.Stage(Box(Stack(screens, Space(control, d.H(27)), Space(d.Small(d.Text("note"), TextOn(enamel), 24, TextAlignment.Center, Pixel), 15)),
            enamel, 34, 40, Darken(enamel, .55)), C("#464C6E"), Colors.White, w);
    }

    private static FrameworkElement Encounter(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1180 : 895);
        var forest = C("#243B40");
        var peach = C("#F4D0A3");
        var moss = C("#B7DDC3");
        var dark = C("#192E36");
        var figure = d.Photo(d.Art(310), 10) ?? Artwork(d.Art(320), C("#DAA06B"), 1);
        var stage = Box(Stack(Pair(d.Small(d.Text("note").ToUpperInvariant(), moss, 26, font: Pixel),
            d.Small("Lv. " + d.Text("level"), peach, 29, TextAlignment.Right, Pixel), .72),
            Space(figure, 22, 22), d.TitleText(d.Title, Colors.White, 52, Pixel), d.Small(d.Artist, moss, 25, font: Pixel)),
            forest, 28, 18, C("#9CCAB5"));
        var dialogue = Box(Stack(d.Small(d.Text("intro"), dark, 33, font: Pixel), Space(d.Lyrics(w - 144, dark, 68, face: PixelFace), 24),
            Space(Rule(C("#AD8978"), dotted: true), 17, 13), d.Small(d.Text("options"), dark, 25, font: Pixel)), peach, 35, 9, C("#665D67"));
        return d.Stage(Box(Stack(stage, Space(dialogue, 17)), C("#171F32"), 29, 24, C("#53636A")), C("#415854"), Colors.White, w);
    }

    private static FrameworkElement Inventory(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1290 : 876);
        var back = C("#292535");
        var frame = C("#46374A");
        var brass = C("#D9BD82");
        var lavender = C("#EBDDDE");
        var rarity = Box(Stack(Artwork(d.Art(255), C("#8669B2"), 0), Space(d.Small(d.Text("rating"), brass, 33, TextAlignment.Center), 10)), C("#211B2D"), 17, 7, brass);
        var item = Stack(d.Small(d.Text("rarity").ToUpperInvariant() + "  /  " + d.Text("item"), brass, 26), Space(d.TitleText(d.Title, lavender, 58, DmSerif), 11),
            d.Small(d.Artist, lavender, 27), Space(d.Small(d.Text("note"), brass, 27, font: DmSerif), 15));
        var details = Stack(Pair(rarity, item, .35, 30), Space(Rule(brass, true), 25, 22),
            d.Lyrics(w - 124, lavender, 69),
            Space(Pair(d.Small("VALUE  " + d.Text("value"), brass, 24), d.Small("TRADEABLE  " + d.Text("trade"), brass, 24, TextAlignment.Right)), 28));
        return d.Stage(Box(details, frame, 53, 14, brass), back, brass, w,
            new RadialGradientBrush(C("#5E4965"), C("#201D2B")));
    }

    private static FrameworkElement FantasyCard(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1200 : 854);
        var forest = Darken(d.Accent, .32);
        var brass = C("#CCA969");
        var parchment = C("#F1E2C2");
        var title = Box(Pair(d.TitleText(d.Title, Ink, 49, DmSerif), d.Small(d.Text("symbol"), Ink, 35, TextAlignment.Right), .74),
            parchment, 17, 11, brass);
        var art = d.Photo(d.Art(380), 5) ?? Artwork(d.Art(315), Lighten(d.Accent, .48), 1);
        var type = d.Text("type") + "  ·  " + d.Text("category");
        var description = Box(Stack(d.Small(type, Ink, 26), Space(Rule(C("#9B7957")), 14, 20), d.Lyrics(w - 176, Ink, 69),
            Space(Pair(d.Small(d.Text("rarity"), C("#6E5036"), 25), d.Small(d.Text("id"), C("#6E5036"), 25, TextAlignment.Right, Mono)), 23),
            Space(d.Small(d.Artist, C("#6E5036"), 24, TextAlignment.Right), 10)), parchment, 36, 11, brass);
        return d.Stage(Box(Stack(title, Space(Box(art, forest, 11, 8, brass), 16, 16), description), forest, 37, 24, brass),
            C("#332C38"), Colors.White, w);
    }

    private static FrameworkElement Chess(Studio d)
    {
        var w = Math.Min(d.Width, d.Wide ? 1340 : 882);
        var light = d.Color("light", C("#F1D9AD"));
        var dark = d.Color("dark", C("#AA7A5F"));
        var ink = C("#342824");
        var sand = C("#E8D1AF");
        var size = d.Art(d.Wide ? 491 : 462);
        // Position after 1.e4 e5 2.Nf3 Nc6 3.Bb5.
        string[] ranks = ["♜·♝♛♚♝♞♜","♟♟♟♟·♟♟♟","··♞·····","·♗··♟···","····♙···","·····♘··","♙♙♙♙·♙♙♙","♖♘♗♕♔··♖"];
        var grid = new UniformGrid { Rows = 8, Columns = 8, Height = size, Width = size, HorizontalAlignment = HorizontalAlignment.Center };
        for (var r = 0; r < 8; r++)
            for (var c = 0; c < 8; c++)
            {
                var square = r == 3 && c == 1 ? C("#DAB456") : (r + c) % 2 == 0 ? light : dark;
                var piece = ranks[r][c];
                grid.Children.Add(Box(d.Small(piece == '·' ? "" : piece.ToString(), TextOn(square), Math.Max(13, size / 11),
                    TextAlignment.Center, new FontFamily("Segoe UI Symbol")), square));
            }
        var board = Box(Stack(grid, Space(d.Small("a   b   c   d   e   f   g   h", ink, 26, TextAlignment.Center, Mono), 8)),
            sand, 15, 3, C("#6F5142"));
        var notes = Stack(d.Small("3. Bb5   /   GAME NOTES", C("#895C50"), 24, font: Mono),
            Space(d.TitleText(d.Text("title"), ink, 54, DmSerif), 12),
            Space(d.Lyrics(d.Wide ? w * .53 - 75 : w - 110, ink, 65), 22),
            Space(d.Small(d.Text("note"), ink, 25, font: DmSerif), 18),
            Space(d.Small("RESULT  " + d.Text("result"), C("#895C50"), 24, font: Mono), 13),
            Space(d.Song(ink, 25), 10));
        var spread = d.Wide ? Pair(board, notes, .43, 36) : Stack(board, Space(notes, 31));
        return d.Stage(Box(spread, C("#F1E3CF"), 48, 5), C("#574139"), Colors.White, w);
    }
}
