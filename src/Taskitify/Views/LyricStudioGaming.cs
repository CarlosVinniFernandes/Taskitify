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

    private static FrameworkElement RoundKeys(Studio d, string[] captions, Color color, double size)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var label in captions)
        {
            var face = d.Small(label, TextOn(color), Math.Min(30, size * .36), TextAlignment.Center, Pixel);
            face.VerticalAlignment = VerticalAlignment.Center;
            var button = Enamel(face, color, new Thickness(0), new CornerRadius(size / 2));
            button.Width = button.Height = size;
            button.Margin = new Thickness(6, 0, 0, 0);
            strip.Children.Add(button);
        }
        return strip;
    }


    private static FrameworkElement PocketGame(Studio d)
    {
        var w = d.Wide ? 1080 : 894;
        var shell = d.Color("device", C("#DBD5C6"));
        var mark = TextOn(shell);
        var screen = d.Color("screen", C("#C2D195"));
        var ink = Luminance(screen) > .48 ? C("#253B29") : Colors.White;
        var raspberry = C("#923E6A");
        var gray = C("#454955");
        var lcd = Recess(Stack(d.Small(d.Text("note"), ink, 26, font: Pixel),
            Space(d.Lyrics(w - 225, ink, 68, face: PixelFace), 24),
            Space(d.Song(ink, 24), 19)), screen, 32, 6);
        lcd.Background = LinedPaper(screen, Alpha(ink, 25), 11, grid: true);
        var header = Pair(d.Small("●   POWER", C("#F1AEC1"), 23, font: Pixel),
            Space(Rule(C("#A45F88"), weight: 5), 9), .30);
        var bezel = Recess(Stack(header, Space(lcd, 18)), gray, 22, 24);
        var actions = RoundKeys(d, ["B", "A"], raspberry, d.H(93));
        actions.RenderTransform = new RotateTransform(-21);
        actions.RenderTransformOrigin = new Point(.5, .5);
        var thumb = Pair(Dpad(C("#353846"), d.H(138)), actions, .51, 30);
        var pills = RoundKeys(d, ["SELECT", "START"], C("#8D8990"), d.H(52));
        var foot = Pair(pills, Vents(C("#504A51"), 6, 10, d.H(63)), .66);
        var device = Enamel(Stack(bezel, Space(d.TitleText(d.Text("label"), mark, 39), 22), Space(thumb, d.H(51)), Space(foot, d.H(41)),
            Space(Screw(C("#908780"), 20), 18)), shell, new Thickness(39, 42, 39, 38), new CornerRadius(32, 32, 124, 38));
        return d.Stage(Mounted(device, 30), C("#5E536D"), Colors.White, w,
            Desk(C("#A19BB4"), C("#4A435A")), inset: 66, footerGap: 42);
    }


    private static FrameworkElement DualScreen(Studio d)
    {
        var w = d.Wide ? 1430 : 952;
        var shell = d.Color("device", C("#8D9BC2"));
        var dark = C("#26314B");
        var fg = C("#EBF1F8");
        var blue = C("#74E2DA");
        var paper = C("#D9E8EA");
        var copy = Stack(d.Small("NOW PLAYING", blue, 24, font: Pixel), d.TitleText(d.Title, fg, 46),
            d.Small(d.Artist, fg, 27), Space(d.Progress(blue, C("#52617C"), 6), 20),
            Space(d.Small((d.Timestamp ? d.Time + "  /  " : "") + d.Duration, fg, 22, font: Pixel), 13));
        var cover = d.Photo(d.Art(d.Story ? 268 : 184), 5);
        FrameworkElement upper = cover is null ? copy : Pair(cover, copy, .36, 24);
        var top = Enamel(Stack(Recess(upper, dark, 29, 15),
            Space(Pair(Vents(Darken(shell, .47), 5, 6, d.H(24)), d.Small("●   LINK", TextOn(shell), 22, TextAlignment.Right, Pixel)), 20)),
            shell, new Thickness(30, 29, 30, 24), new CornerRadius(42, 42, 21, 21));
        var inner = d.Wide ? (w - 67) * .53 - 137 : w - 144;
        var touch = Recess(Stack(d.Small(d.Text("intro"), C("#37556B"), 30, font: Pixel),
            Space(d.Lyrics(inner, Ink, 69, face: PixelFace), 20),
            Space(d.Small("▼", C("#37556B"), 25, TextAlignment.Right, Pixel), 16)), paper, 30, 14);
        var controls = Pair(Dpad(dark, d.H(83)), RoundKeys(d, ["Y", "X", "B", "A"], dark, d.H(53)), .45, 22);
        var lower = Enamel(Stack(touch, Space(controls, d.H(29)),
            Space(d.Small(d.Text("note"), TextOn(shell), 23, TextAlignment.Center, Pixel), 19)), shell,
            new Thickness(31, 26, 31, 28), new CornerRadius(21, 21, 46, 46));
        var hinge = new Grid { Height = d.H(40) };
        hinge.Children.Add(Enamel(new Grid(), Darken(shell, .50), new Thickness(0), new CornerRadius(13)));
        hinge.Children.Add(new Border { Width = w * .5, Background = new LinearGradientBrush(Lighten(shell, .16), Darken(shell, .67), 90) });
        FrameworkElement system = d.Wide ? Pair(top, lower, .47, 28) : Stack(top, hinge, lower);
        return d.Stage(system, C("#373F61"), Colors.White, w, Desk(C("#8798BB"), C("#303650")), inset: 46, footerGap: 39);
    }


    private static FrameworkElement Encounter(Studio d)
    {
        var w = d.Wide ? 1330 : 984;
        var dirt = C("#203949");
        var mint = C("#C8E5BA");
        var ink = C("#252F3C");
        var terrain = new Grid { Height = d.Art(d.Story ? 430 : 304) };
        terrain.Background = LinedPaper(C("#53816A"), C("#618E75"), 27, grid: true);
        var clearing = new Ellipse { Fill = Brush(Alpha(mint, 110)), Width = w * .68, Height = d.H(92),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14) };
        terrain.Children.Add(clearing);
        if (d.Photo(d.Art(d.Story ? 355 : 244), 3, d.Art(d.Story ? 355 : 244)) is { } portrait)
        {
            var frame = Mounted(Box(portrait, mint, 6, 4, dirt), 13);
            frame.HorizontalAlignment = HorizontalAlignment.Center;
            frame.VerticalAlignment = VerticalAlignment.Center;
            terrain.Children.Add(frame);
        }
        else terrain.Children.Add(Artwork(d.Art(281), C("#E3A372"), 1));
        var user = Box(Pair(Stack(d.TitleText(d.Title, mint, 44, Pixel), d.Small(d.Artist, mint, 24, font: Pixel)),
            Stack(d.Small("Lv. " + d.Text("level"), mint, 29, TextAlignment.Right, Pixel),
                d.Small(d.Text("note"), mint, 23, TextAlignment.Right, Pixel)), .68), dirt, 19, 4, C("#7FA78D"));
        var view = Stack(Box(terrain, C("#659180"), 7, 3, dirt), Space(user, 11));
        var textWidth = d.Wide ? w * .55 - 106 : w - 135;
        var console = Box(Stack(d.Small(d.Text("intro"), C("#745B60"), 34, font: Pixel),
            Space(d.Lyrics(textWidth, ink, 71, face: PixelFace), 23),
            Space(Rule(C("#A88476"), dotted: true), 21, 16),
            d.Small("▸  " + d.Text("options"), ink, 25, font: Pixel)), C("#FAE2B8"), 37, 5, C("#775A64"));
        var game = d.Wide ? Pair(view, console, .45, 21) : Stack(view, Space(console, 21));
        return d.Stage(Mounted(Box(game, dirt, 27, 16, C("#799A82")), 26), dirt, mint, w,
            Desk(C("#577965"), C("#233946")), inset: 47);
    }


    private static FrameworkElement Inventory(Studio d)
    {
        var w = d.Wide ? 1320 : 952;
        var bronze = C("#D3AD70");
        var night = C("#211D2C");
        var violet = C("#3A2B43");
        var paper = C("#F3E7DE");
        var bar = Box(Pair(d.Small(d.Text("rarity").ToUpperInvariant() + "  /  " + d.Text("item"), bronze, 28),
            d.Small("◇   ◈   ◇", bronze, 26, TextAlignment.Right), .8), C("#332736"), 19, 2, bronze);
        var gems = Box(Artwork(d.Art(d.Story ? 378 : 296), C("#9273C3"), 0), night, 13, 3, bronze);
        var item = Stack(d.TitleText(d.Title, paper, 65, DmSerif), d.Small(d.Artist, bronze, 29),
            Space(d.Small(d.Text("rating"), bronze, 36), 12), Space(d.Small(d.Text("note"), paper, 26, font: DmSerif), 18));
        var attributes = Pair(Box(d.Small("VALUE   " + d.Text("value"), bronze, 26), night, 15, 4, C("#654C5A")),
            Box(d.Small("TRADE   " + d.Text("trade"), bronze, 26, TextAlignment.Right), night, 15, 4, C("#654C5A")), .5, 12);
        var inventory = Stack(bar, Space(Pair(gems, item, .43, 30), 26), Space(attributes, 24),
            Space(Box(d.Lyrics(w - 161, paper, 75), night, 29, 2, C("#8E6E63")), d.H(36)));
        var casework = Box(Box(inventory, violet, 34, 4, C("#99785D")), night, 13, 11, bronze);
        return d.Stage(Mounted(casework, 28), night, bronze, w, Desk(C("#5A4666"), night), inset: 53);
    }


    private static FrameworkElement FantasyCard(Studio d)
    {
        var w = d.Wide ? 1250 : 926;
        var forest = Darken(d.Accent, .33);
        var gold = C("#CDA971");
        var parchment = C("#F5E8CC");
        var black = C("#252320");
        var header = Box(Pair(d.TitleText(d.Title, black, 55, DmSerif),
            d.Small(d.Text("symbol"), black, 33, TextAlignment.Right), .77), parchment, 17, 10, gold);
        var art = d.Photo(d.Art(d.Story ? 506 : 366), 5) ?? Artwork(d.Art(353), Lighten(d.Accent, .42), 1);
        var artFrame = Box(art, Darken(forest, .52), 9, 6, gold);
        var type = Box(Pair(d.Small(d.Text("type") + "  ·  " + d.Text("category"), black, 28, font: DmSerif),
            d.Small("✦   " + d.Text("rarity"), black, 24, TextAlignment.Right), .76), C("#D9C5A3"), 14, 5, gold);
        var width = d.Wide ? (w - 92) * .56 - 96 : w - 185;
        var lore = Box(Stack(d.Lyrics(width, black, 74),
            Space(Rule(C("#B7946A")), 20, 14),
            Pair(d.Small(d.Artist, C("#675045"), 25), d.Small(d.Text("id"), C("#675045"), 23, TextAlignment.Right, Mono), .66)),
            parchment, 32, 8, gold);
        lore.Background = Fibers(parchment, Alpha(C("#A47C62"), 24));
        var lower = Stack(type, Space(lore, 11));
        var arrangement = d.Wide ? Pair(artFrame, lower, .44, 20) : Stack(artFrame, Space(lower, 14));
        var inner = Box(Stack(header, Space(arrangement, 13)), forest, 24, 17, gold);
        return d.Stage(Mounted(Enamel(inner, Darken(forest, .55), new Thickness(15), new CornerRadius(29)), 29),
            C("#332934"), Colors.White, w, Desk(C("#67505D"), C("#322535")), inset: 44, footerGap: 34);
    }

    private static FrameworkElement Chess(Studio d)
    {
        var w = d.Wide ? 1410 : 969;
        var light = d.Color("light", C("#F1D9AD"));
        var dark = d.Color("dark", C("#AA7A5F"));
        var ink = C("#342824");
        var sand = C("#E8D1AF");
        var size = d.Art(d.Wide ? 549 : d.Story ? 695 : 540);
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
        var files = new UniformGrid { Columns = 8, Width = size, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var file in "abcdefgh")
            files.Children.Add(d.Small(file.ToString(), ink, 24, TextAlignment.Center, Mono));
        var board = Mounted(Enamel(Stack(grid, Space(files, 10)), sand, new Thickness(19), new CornerRadius(4)), 16);
        var notes = Stack(d.Small("3. Bb5   /   GAME NOTES", C("#895C50"), 24, font: Mono),
            Space(d.TitleText(d.Text("title"), ink, 54, DmSerif), 12),
            Space(d.Lyrics(d.Wide ? w * .53 - 75 : w - 110, ink, 65), 22),
            Space(d.Small(d.Text("note"), ink, 25, font: DmSerif), 18),
            Space(d.Small("RESULT  " + d.Text("result"), C("#895C50"), 24, font: Mono), 13),
            Space(d.Song(ink, 25), 10));
        var masthead = Pair(d.Small("POSITION STUDIES", ink, 26, font: Mono), d.Small("OPENING  /  III", ink, 25, TextAlignment.Right, Mono));
        var spread = d.Wide ? Pair(board, notes, .43, 37) : Stack(board, Space(notes, d.H(47)));
        var print = Box(Stack(masthead, Space(Rule(C("#956E50")), 14, 25), spread), C("#F1E3CF"), 49, 3);
        print.Background = Fibers(C("#F1E3CF"), Alpha(ink, 21));
        return d.Stage(Mounted(print, 23), C("#574139"), Colors.White, w,
            Desk(C("#94745D"), C("#45332F")), inset: 51);
    }
}
