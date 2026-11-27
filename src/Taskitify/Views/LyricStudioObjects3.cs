using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Taskitify.Views;

/// <summary>
/// A third set of styles drawn as objects: a telegram with its message pasted on in strips of
/// printed tape, a 3.5-inch diskette with a label written by hand, and a click-wheel player
/// showing the lines on its lyrics screen.
/// </summary>
public static partial class LyricCardRenderer
{
    // ======================= Telegram =======================

    /// <summary>
    /// A telegram form as it arrived: the preprinted yellow blank (office name, class boxes, the
    /// small print), the message printed by the teleprinter on paper tape, cut into strips and
    /// pasted on a little crooked, each line ending in STOP; the office's dated stamp in violet
    /// ink, the delivery stamp, and a pencilled time of receipt.
    /// </summary>
    private static FrameworkElement Telegraph(Studio d)
    {
        const double w = 900;
        var paper = C("#F2E3B3");
        var printed = C("#6B4A1E");
        var tapeInk = C("#1E1E22");
        var violet = C("#5B3C8C");
        var red = C("#B3262E");
        var face = d.UserFace;
        var canvas = new Canvas { Width = w };

        // The message: one strip of tape per printed line, words never cut, STOP at each line's end.
        var size = Presence(d, 34);
        var font = face?.Family ?? Courier;
        var weight = face?.Weight ?? FontWeights.Bold;
        var maxStrip = w - 190;
        var strips = new List<string>();
        foreach (var raw in d.LyricText.Split('\n'))
        {
            var words = (raw.Trim().ToUpper(CultureInfo.CurrentCulture) + " STOP").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var next = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && MeasureWidth(next, font, size, weight) + 40 > maxStrip)
                {
                    strips.Add(current);
                    current = word;
                }
                else
                    current = next;
            }
            if (current.Length > 0)
                strips.Add(current);
        }
        var stripHeight = size * 1.62;
        var messageTop = 520.0;
        var h = Math.Max(1020, messageTop + (strips.Count * (stripHeight + 14)) + 330);
        canvas.Height = h;

        var sheet = new Grid { Width = w, Height = h, Background = Fibers(paper, Alpha(C("#A88A4A"), 40)) };
        sheet.Children.Add(new Border { Background = new RadialGradientBrush(Alpha(C("#FFF6D8"), 0), Alpha(C("#B08A3A"), 70)) { RadiusX = .8, RadiusY = .7 } });
        var form = new Canvas();
        // Preprinted header.
        form.Children.Add(At(Spaced("TASKITIFY TELEGRAPH", DmSerif, 58, FontWeights.Normal, printed, TextAlignment.Left, .06), 60, 46));
        form.Children.Add(At(Spaced("TELEGRAM  ·  CABLEGRAM  ·  RADIOGRAM", Figtree, 15, FontWeights.Bold, Alpha(printed, 200), TextAlignment.Left, .3), 64, 122));
        form.Children.Add(At(new Rectangle { Width = w - 120, Height = 3, Fill = Brush(printed) }, 60, 156));
        form.Children.Add(At(new Rectangle { Width = w - 120, Height = 1.2, Fill = Brush(printed) }, 60, 163));
        // Class of service boxes, one ticked by the clerk.
        var classes = new[] { "FULL RATE", "DAY LETTER", "NIGHT MESSAGE", "NIGHT LETTER" };
        for (var i = 0; i < classes.Length; i++)
        {
            var x = 60 + (i * 196);
            form.Children.Add(At(new Rectangle { Width = 22, Height = 22, Stroke = Brush(printed), StrokeThickness = 1.6 }, x, 192));
            form.Children.Add(At(Spaced(classes[i], Figtree, 14, FontWeights.Bold, printed, TextAlignment.Left, .18), x + 32, 195));
        }
        form.Children.Add(Pen(Geometry.Parse("M260,196 L268,212 L288,186"), C("#2B2B33"), 3.4));
        form.Children.Add(At(Label("Patrons should check class of service desired; otherwise the message will be sent at full rate.", Georgia, 15, FontWeights.Normal, Alpha(printed, 190), TextAlignment.Left), 60, 232));
        // Address block with preprinted labels and teleprinted data.
        FrameworkElement Field(string label, string value, double x, double y, double width)
        {
            var g = new Canvas();
            g.Children.Add(At(Spaced(label, Figtree, 13, FontWeights.Bold, printed, TextAlignment.Left, .25), 0, 0));
            g.Children.Add(At(new Rectangle { Width = width, Height = 1.2, Fill = Brush(Alpha(printed, 160)) }, 0, 52));
            g.Children.Add(At(Thermal(value.ToUpper(CultureInfo.CurrentCulture), 24, tapeInk, x.GetHashCode() % 50, true), 4, 20));
            return At(g, x, y);
        }
        form.Children.Add(Field("FROM", d.Text("from"), 60, 290, 360));
        form.Children.Add(Field("TO", d.Text("to"), 470, 290, 370));
        form.Children.Add(Field("FILED", d.Text("date"), 60, 380, 360));
        form.Children.Add(Field("CHECK", "27 WORDS", 470, 380, 370));
        form.Children.Add(At(Spaced("MESSAGE", Figtree, 13, FontWeights.Bold, printed, TextAlignment.Left, .25), 60, 476));

        // The pasted strips.
        var random = new Random(1844);
        var y = messageTop;
        foreach (var strip in strips)
        {
            var text = Label(strip, font, size, weight, Alpha(tapeInk, 225), TextAlignment.Left);
            text.OpacityMask = Speckle(random.Next(100), .72);
            var width = MeasureWidth(strip, font, size, weight) + 40;
            var tape = new Border
            {
                Width = width, Height = stripHeight, Padding = new Thickness(20, (stripHeight - (size * 1.18)) / 2, 0, 0),
                Background = new LinearGradientBrush(C("#FBF9F2"), C("#ECE8DC"), 90), Child = text,
                RenderTransformOrigin = new Point(0, .5), RenderTransform = new RotateTransform((random.NextDouble() * 1.4) - .7),
                Effect = new DropShadowEffect { Color = C("#5A4420"), BlurRadius = 3, ShadowDepth = 1.5, Opacity = .35 },
            };
            form.Children.Add(At(tape, 80 + (random.NextDouble() * 14), y));
            y += stripHeight + 14;
        }

        // Office stamp in violet ink, the delivery stamp, a pencilled note of the time.
        var office = new Grid { Width = 230, Height = 230, Opacity = .78, RenderTransform = new RotateTransform(-11), RenderTransformOrigin = new Point(.5, .5), OpacityMask = Speckle(61, .45) };
        office.Children.Add(new Ellipse { Stroke = Brush(violet), StrokeThickness = 6 });
        office.Children.Add(new Ellipse { Margin = new Thickness(18), Stroke = Brush(violet), StrokeThickness = 2.5 });
        var officeText = Label("MAIN OFFICE\n" + DateTime.Today.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() + "\n" + d.Time, Courier, 22, FontWeights.Bold, violet, TextAlignment.Center);
        officeText.VerticalAlignment = VerticalAlignment.Center;
        office.Children.Add(officeText);
        form.Children.Add(At(office, w - 300, y + 10));
        var delivered = new Border { BorderBrush = Brush(red), BorderThickness = new Thickness(5), Padding = new Thickness(16, 6, 16, 8), Opacity = .82,
            Child = Label(d.Text("stamp").ToUpper(CultureInfo.CurrentCulture), Figtree, 30, FontWeights.Black, red, TextAlignment.Center),
            RenderTransform = new RotateTransform(-6), OpacityMask = Speckle(62, .5) };
        form.Children.Add(At(delivered, 70, y + 60));
        form.Children.Add(At(Handwriting("rec'd " + d.Time + " — " + d.Text("note"), HandFont, 38, C("#5E5B57"), 560, 64, TextAlignment.Left, 44), 76, y + 160));
        // Small print at the foot.
        var foot = Label("THE COMPANY WILL APPRECIATE SUGGESTIONS FROM ITS PATRONS CONCERNING ITS SERVICE  ·  FORM 27-T", Figtree, 12, FontWeights.Bold, Alpha(printed, 170), TextAlignment.Center);
        foot.Width = w - 120;
        form.Children.Add(At(foot, 60, h - 60));
        sheet.Children.Add(form);
        // Folded in three for the envelope.
        foreach (var fold in new[] { h / 3, h * 2 / 3 })
            sheet.Children.Add(new Border { Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, fold - 12, 0, 0),
                Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, 26), .48), new(Alpha(Colors.White, 90), .55), new(Alpha(Colors.White, 0), 1) }, 90) });
        sheet.RenderTransformOrigin = new Point(.5, .5);
        sheet.RenderTransform = new RotateTransform(1.6);
        sheet.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 16, Direction = 280, Opacity = .4 };
        var holder = new Canvas { Width = w + 80, Height = h + 80 };
        holder.Children.Add(At(sheet, 40, 40));
        return ObjectStage(d, holder, holder.Width, holder.Height, Desk(C("#6E5A44"), C("#2A2018")), C("#F2E3B3"), C("#2A2018"));
    }

    // ======================= Diskette =======================

    /// <summary>
    /// A 3.5-inch diskette at its real proportions (90 x 94 mm): moulded plastic with the cut
    /// corner, the brushed metal shutter with its window, the insertion arrow, the write-protect
    /// slider and density hole, and a paper label written by hand with the lines.
    /// </summary>
    private static FrameworkElement Diskette(Studio d)
    {
        const double w = 900, h = 940;
        var body = d.Color("device", C("#24272F"));
        var light = Luminance(body) > .55;
        var pen = C("#1F2F6E");
        var face = d.UserFace;
        var canvas = new Canvas { Width = w, Height = h };
        var outline = Geometry.Parse("M24,0 H846 L900,54 V916 Q900,940 876,940 H24 Q0,940 0,916 V24 Q0,0 24,0 Z");
        canvas.Children.Add(new Path { Data = outline, Fill = new LinearGradientBrush(new GradientStopCollection { new(Lighten(body, .14), 0), new(body, .5), new(Darken(body, .8), 1) }, new Point(0, 0), new Point(1, 1)),
            Stroke = Brush(Darken(body, .6)), StrokeThickness = 2 });
        canvas.Children.Add(new Path { Data = outline, Fill = Fibers(Alpha(Colors.White, 0), Alpha(light ? Colors.Black : Colors.White, 14), 1.2) });
        // Recess for the label, and for the shutter.
        canvas.Children.Add(At(new Border { Width = 680, Height = 560, CornerRadius = new CornerRadius(10), Background = Brush(Darken(body, .86)),
            BorderBrush = new LinearGradientBrush(Darken(body, .6), Lighten(body, .2), 90), BorderThickness = new Thickness(2) }, 110, 340));
        // Shutter: brushed aluminium, a window to the magnetic disc, the spring slot.
        var shutter = new Grid { Width = 560, Height = 300 };
        var brushed = new DrawingGroup();
        brushed.Children.Add(new GeometryDrawing(new LinearGradientBrush(C("#E4E6EA"), C("#A9ADB5"), 90), null, new RectangleGeometry(new Rect(0, 0, 200, 40))));
        var streaks = new Random(35);
        for (var k = 0; k < 40; k++)
            brushed.Children.Add(new GeometryDrawing(null, new System.Windows.Media.Pen(Brush(Alpha(streaks.Next(2) == 0 ? Colors.White : Colors.Black, (byte)(20 + streaks.Next(40)))), .6), new LineGeometry(new Point(0, k), new Point(200, k + (streaks.NextDouble() - .5)))));
        shutter.Background = new DrawingBrush(brushed) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 200, 40), ViewportUnits = BrushMappingMode.Absolute };
        shutter.Children.Add(new Border { BorderBrush = new LinearGradientBrush(Colors.White, C("#6E737C"), 90), BorderThickness = new Thickness(2) });
        var window = new Border { Width = 130, Height = 230, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 30, 110, 40), CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush(C("#3A2E26"), C("#1A1410"), 0), BorderBrush = Brush(C("#7A7E86")), BorderThickness = new Thickness(2) };
        shutter.Children.Add(window);
        canvas.Children.Add(At(shutter, 170, 0));
        canvas.Children.Add(At(new Border { Width = 40, Height = 120, CornerRadius = new CornerRadius(4), Background = Brush(Darken(body, .7)) }, 120, 30));
        // Insertion arrow, embossed; the density hole and the write-protect slider.
        canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,10 L28,10 L28,0 L48,18 L28,36 L28,26 L0,26 Z"), Fill = Brush(Lighten(body, .25)), RenderTransform = new RotateTransform(-90) }, 46, 140));
        canvas.Children.Add(At(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(3), Background = Brush(C("#0C0C0E")) }, 824, 860));
        canvas.Children.Add(At(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(3), Background = Brush(C("#0C0C0E")),
            Child = new Border { Width = 46, Height = 22, VerticalAlignment = VerticalAlignment.Bottom, Background = Brush(Lighten(body, .2)) } }, 30, 860));
        canvas.Children.Add(At(Label("HD", Figtree, 26, FontWeights.Black, Lighten(body, .3), TextAlignment.Left), 826, 818));
        // The label: preprinted stripe and rules; the file name and the lines in pen.
        var label = new Grid { Width = 640, Height = 520, Background = Fibers(C("#FBFAF5"), Alpha(C("#C2B391"), 22)) };
        label.Children.Add(new Border { Height = 64, VerticalAlignment = VerticalAlignment.Top, Background = new LinearGradientBrush(Lighten(d.Accent, .1), Darken(d.Accent, .7), 0) });
        var brand = new DockPanel { Height = 64, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(22, 0, 22, 0) };
        var cap = Label(d.Text("id"), Figtree, 22, FontWeights.Black, Colors.White, TextAlignment.Right);
        cap.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(cap, Dock.Right);
        brand.Children.Add(cap);
        var mark = Spaced("TASKITIFY  2HD  ·  " + d.Text("label").ToUpper(CultureInfo.CurrentCulture), Figtree, 15, FontWeights.Bold, Colors.White, TextAlignment.Left, .16);
        mark.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(mark);
        label.Children.Add(brand);
        var shortText = d.LyricText.Length <= 50;
        var rule = shortText ? 78.0 : 60.0;
        var rules = new DrawingGroup();
        rules.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 10, rule))));
        rules.Children.Add(new GeometryDrawing(Brush(Alpha(C("#7E9DC4"), 110)), null, new RectangleGeometry(new Rect(0, rule - 1.4, 10, 1.4))));
        label.Children.Add(new Border { Margin = new Thickness(26, 140, 26, 20), Background = new DrawingBrush(rules) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, rule), ViewportUnits = BrushMappingMode.Absolute } });
        var writing = new Canvas();
        writing.Children.Add(At(Handwriting(d.Text("file"), HandFont, 50, Alpha(pen, 240), 580, 71, TextAlignment.Left, 60, maxHeight: 64), 34, 74));
        writing.Children.Add(At(Handwriting(d.LyricText, face?.Family ?? HandFont, Math.Min(Presence(d, 62), rule * 1.12), pen, 580, 72, d.Align(TextAlignment.Left), rule,
            maxHeight: Math.Floor(340 / rule) * rule, weight: face?.Weight ?? FontWeights.SemiBold, preferWhole: d.LyricText.Length <= 110), 34, 148));
        label.Children.Add(writing);
        label.RenderTransformOrigin = new Point(.5, .5);
        label.RenderTransform = new RotateTransform(-.6);
        canvas.Children.Add(At(label, 130, 360));
        // A sticker dot in the corner: the way a disk was told apart in a box of them.
        canvas.Children.Add(At(new Ellipse { Width = 70, Height = 70, Fill = new RadialGradientBrush(C("#FF8A7A"), C("#D9342B")), Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Opacity = .3 } }, 100, 830));
        canvas.Children.Add(At(Label("♪", Symbols, 34, FontWeights.Normal, Colors.White, TextAlignment.Center), 119, 842));
        canvas.RenderTransformOrigin = new Point(.5, .5);
        canvas.RenderTransform = new RotateTransform(-3);
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 50, ShadowDepth = 20, Direction = 285, Opacity = .5 };
        var holder = new Canvas { Width = w + 80, Height = h + 80 };
        holder.Children.Add(At(canvas, 40, 40));
        return ObjectStage(d, holder, holder.Width, holder.Height, Desk(C("#7B8794"), C("#2B323A")), Colors.White, C("#2B323A"));
    }

    // ======================= Click-wheel player =======================

    /// <summary>
    /// A click-wheel music player of 2005 at its proportions: the glossy face over a polished
    /// steel back that shows at the edges, the 2.5-inch screen on its lyrics view (status bar,
    /// cover and song, the lines, the progress bar), and the wheel with MENU, skip and play.
    /// </summary>
    private static FrameworkElement ClickWheel(Studio d)
    {
        const double w = 760, h = 1270;
        var black = d.Text("model") == "Black";
        var faceColor = d.Color("device", black ? C("#1A1B1F") : C("#F6F6F4"));
        var dark = Luminance(faceColor) < .4;
        var userFace = d.UserFace;
        var canvas = new Canvas { Width = w, Height = h };
        // Polished steel back showing around the face.
        canvas.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(70),
            Background = new LinearGradientBrush(new GradientStopCollection { new(C("#F4F5F7"), 0), new(C("#9A9EA6"), .3), new(C("#E8EAEE"), .55), new(C("#7C8088"), .8), new(C("#D6D9DE"), 1) }, 0) });
        canvas.Children.Add(At(new Border { Width = w - 14, Height = h - 14, CornerRadius = new CornerRadius(64),
            Background = new LinearGradientBrush(Lighten(faceColor, dark ? .1 : .3), Darken(faceColor, dark ? .7 : .93), 90) }, 7, 7));
        // Screen.
        const double sx = 80, sy = 82, sw = 600, sh = 450;
        canvas.Children.Add(At(new Border { Width = sw + 24, Height = sh + 24, CornerRadius = new CornerRadius(10), Background = Brush(C("#0E0F12")) }, sx - 12, sy - 12));
        var screen = new Grid { Width = sw, Height = sh, Background = Brush(Colors.White), ClipToBounds = true };
        screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        screen.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        screen.RowDefinitions.Add(new RowDefinition());
        screen.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new Grid { Background = new LinearGradientBrush(new GradientStopCollection { new(C("#FDFDFD"), 0), new(C("#D9DDE3"), .5), new(C("#C2C8D0"), 1) }, 90) };
        bar.Children.Add(new Border { BorderBrush = Brush(C("#8A9098")), BorderThickness = new Thickness(0, 0, 0, 1.5) });
        var barTitle = Label("Lyrics", InterDisplay, 24, FontWeights.Bold, C("#1A1A1A"), TextAlignment.Center);
        barTitle.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(barTitle);
        var play = Label("▶", Symbols, 18, FontWeights.Normal, C("#2B6CD8"), TextAlignment.Left);
        play.Margin = new Thickness(16, 0, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(play);
        var battery = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 16, 0), BorderBrush = Brush(C("#3A3A3A")), BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(2), Child = new Border { Width = 26, HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(C("#7BE07A"), C("#2E9E2C"), 90) } };
        bar.Children.Add(battery);
        screen.Children.Add(bar);
        var song = new DockPanel { Margin = new Thickness(18, 14, 18, 0) };
        var cover = d.Photo(78, 2, 78) ?? new Border { Width = 78, Height = 78, Background = new LinearGradientBrush(d.Accent, Darken(d.Accent, .5), 45) };
        cover.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(cover, Dock.Left);
        song.Children.Add(cover);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Label(d.Title, InterDisplay, 24, FontWeights.Bold, C("#111111"), TextAlignment.Left));
        names.Children.Add(Label(d.Artist, InterDisplay, 21, FontWeights.Normal, C("#555A62"), TextAlignment.Left));
        song.Children.Add(names);
        Grid.SetRow(song, 1);
        screen.Children.Add(song);
        var lyric = LyricLines(d.LyricText, userFace?.Family ?? InterDisplay, userFace?.Weight ?? FontWeights.Bold, C("#111111"), sw - 44, 230, Presence(d, 44), 20,
            d.Align(TextAlignment.Left), Leading(d, 1.14), gap: 6, wholeShrink: .66);
        lyric.Margin = new Thickness(22, 0, 22, 0);
        lyric.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(lyric, 2);
        screen.Children.Add(lyric);
        var progress = new DockPanel { Margin = new Thickness(18, 0, 18, 16) };
        var start = Label(d.Timestamp ? d.Time : "0:00", InterDisplay, 18, FontWeights.SemiBold, C("#333333"), TextAlignment.Left);
        DockPanel.SetDock(start, Dock.Left);
        progress.Children.Add(start);
        var end = Label("-" + d.Duration, InterDisplay, 18, FontWeights.SemiBold, C("#333333"), TextAlignment.Right);
        DockPanel.SetDock(end, Dock.Right);
        progress.Children.Add(end);
        var track = d.Progress(C("#3C7EE6"), C("#D3D8DF"), 12);
        track.Margin = new Thickness(12, 4, 12, 0);
        progress.Children.Add(track);
        Grid.SetRow(progress, 3);
        screen.Children.Add(progress);
        canvas.Children.Add(At(screen, sx, sy));
        canvas.Children.Add(At(new Border { Width = sw, Height = sh, Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(Colors.White, 50), 0), new(Alpha(Colors.White, 0), .45) }, new Point(0, 0), new Point(1, 1)) }, sx, sy));
        // The click wheel.
        const double wheel = 470, wx = (w - wheel) / 2, wy = 640;
        var wheelColor = dark ? C("#2A2B30") : C("#FFFFFF");
        var print = dark ? C("#C9CBD2") : C("#9DA2AA");
        canvas.Children.Add(At(new Ellipse { Width = wheel, Height = wheel, Fill = new RadialGradientBrush(wheelColor, Darken(wheelColor, dark ? .8 : .94)) { GradientOrigin = new Point(.45, .35) },
            Stroke = Brush(Darken(faceColor, dark ? .7 : .86)), StrokeThickness = 2, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = .18 } }, wx, wy));
        var centre = 170.0;
        canvas.Children.Add(At(new Ellipse { Width = centre, Height = centre, Fill = new LinearGradientBrush(Lighten(faceColor, dark ? .12 : .4), Darken(faceColor, dark ? .8 : .9), 90),
            Stroke = Brush(Darken(faceColor, dark ? .6 : .82)), StrokeThickness = 2 }, wx + ((wheel - centre) / 2), wy + ((wheel - centre) / 2)));
        var menu = Label("MENU", Figtree, 30, FontWeights.Bold, print, TextAlignment.Center);
        menu.Width = 200;
        canvas.Children.Add(At(menu, (w / 2) - 100, wy + 36));
        canvas.Children.Add(At(Label("⏮", Symbols, 34, FontWeights.Normal, print, TextAlignment.Left), wx + 34, wy + (wheel / 2) - 24));
        canvas.Children.Add(At(Label("⏭", Symbols, 34, FontWeights.Normal, print, TextAlignment.Left), wx + wheel - 74, wy + (wheel / 2) - 24));
        var playPause = Label("▶❚❚", Symbols, 26, FontWeights.Normal, print, TextAlignment.Center);
        playPause.Width = 200;
        canvas.Children.Add(At(playPause, (w / 2) - 100, wy + wheel - 72));
        var engrave = Label(d.Text("engrave"), Georgia, 22, FontWeights.Normal, Alpha(print, 200), TextAlignment.Center);
        engrave.FontStyle = FontStyles.Italic;
        engrave.Width = w - 100;
        canvas.Children.Add(At(engrave, 50, h - 84));
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 24, Direction = 285, Opacity = .45 };
        // Earbud cable leaving the top.
        var holder = new Canvas { Width = w + 160, Height = h + 200 };
        holder.Children.Add(new Path { Data = Geometry.Parse("M206,30 C190,0 120,10 110,-40 M206,30 C230,-10 320,0 340,-50"), Stroke = Brush(dark ? C("#2A2B30") : Colors.White), StrokeThickness = 9,
            StrokeEndLineCap = PenLineCap.Round, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = .3 }, RenderTransform = new TranslateTransform(0, 70) });
        holder.Children.Add(At(new Border { Width = 40, Height = 30, CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(C("#E8EAEE"), C("#9A9EA6"), 0) }, 186, 76));
        holder.Children.Add(At(canvas, 80, 100));
        return ObjectStage(d, holder, holder.Width, holder.Height, Desk(Lighten(Darken(d.Accent, .5), .3), Darken(d.Accent, .3)), Colors.White, Darken(d.Accent, .3));
    }
}
