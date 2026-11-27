using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Taskitify.Views;

/// <summary>
/// The second set of object styles, at the pilot's bar: each is a scene of its own period and
/// material (a 2005 chat window on the XP hills, an open DS Lite, a mixtape and its J-card, a
/// thermal receipt, a collectible card, a fantasy inventory, a Paint 98 window), with the lines
/// as the thing the object holds.
/// </summary>
public static partial class LyricCardRenderer
{
    private static readonly FontFamily Tahoma = new("Tahoma, Segoe UI");
    private static readonly FontFamily Trebuchet = new("Trebuchet MS, Segoe UI");
    private static readonly FontFamily Arial = new("Arial, Segoe UI");

    /// <summary>
    /// How much presence the lines deserve: a few words get large type and air; many lines get
    /// progressively smaller type and tighter spacing.
    /// </summary>
    private static double Presence(Studio d, double size)
    {
        var text = d.LyricText;
        var chars = text.Length;
        var factor = chars switch { <= 24 => 1.3, <= 45 => 1.16, <= 80 => 1.0, <= 140 => .86, <= 220 => .74, _ => .64 };
        if (text.Split('\n').Length >= 5)
            factor *= .92;
        return size * factor * d.TextScale;
    }

    /// <summary>Line spacing that tightens as the text grows.</summary>
    private static double Leading(Studio d, double factor) => factor * (d.LyricText.Length > 140 ? .94 : d.LyricText.Length < 45 ? 1.04 : 1);

    // ======================= MSN Messenger =======================

    /// <summary>
    /// A 2005 conversation window on the XP hills: the butterfly title bar, the big toolbar,
    /// "To:" with the address, the lines as the contact's messages in the font they chose, their
    /// display picture with the green "available" frame and the "now listening to" message, the
    /// formatting bar and typing box, the status line.
    /// </summary>
    private static FrameworkElement MsnWindow(Studio d)
    {
        const double w = 940, h = 1240;
        var who = d.Text("who");
        var face = d.UserFace;
        var blue = C("#1E4FA8");
        var canvas = new Canvas { Width = w, Height = h };

        var frame = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(12, 12, 4, 4), BorderThickness = new Thickness(4),
            BorderBrush = new LinearGradientBrush(C("#2C66D2"), C("#0B3C9A"), 90),
            Background = new LinearGradientBrush(C("#EEF4FC"), C("#CFDDF2"), 90),
        };
        canvas.Children.Add(frame);
        // Title bar with the butterfly.
        canvas.Children.Add(At(new Border { Width = w - 8, Height = 64, CornerRadius = new CornerRadius(9, 9, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(C("#3D8CF2"), 0), new(C("#155FD6"), .45), new(C("#0E4DBE"), 1) }, 90) }, 4, 4));
        canvas.Children.Add(At(Butterfly(40), 22, 16));
        canvas.Children.Add(At(Label(who + " - Conversation", Tahoma, 25, FontWeights.Bold, Colors.White, TextAlignment.Left), 76, 20));
        foreach (var (glyph, x, red) in new[] { ("–", w - 156.0, false), ("□", w - 108.0, false), ("✕", w - 60.0, true) })
        {
            var cap = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(6), BorderBrush = Brush(Colors.White), BorderThickness = new Thickness(1.5),
                Background = red ? new LinearGradientBrush(C("#E8806A"), C("#C2381E"), 90) : new LinearGradientBrush(C("#6FA4F5"), C("#2A6BDA"), 90),
                Child = Label(glyph, Tahoma, 22, FontWeights.Bold, Colors.White, TextAlignment.Center) };
            ((FrameworkElement)cap.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(cap, x, 15));
        }
        // Toolbar.
        var tools = new[] { ("", "Invite"), ("", "Send Files"), ("", "Webcam"), ("", "Call"), ("", "Activities"), ("", "Games") };
        for (var i = 0; i < tools.Length; i++)
        {
            var x = 36 + (i * 112);
            canvas.Children.Add(At(new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(C("#FFFFFF"), C("#C9DAF3"), 90), BorderBrush = Brush(C("#9DB6DE")), BorderThickness = new Thickness(1.5),
                Child = Glyph(tools[i].Item1, 28, blue, HorizontalAlignment.Center) }, x + 16, 86));
            var label = Label(tools[i].Item2, Tahoma, 17, FontWeights.Normal, C("#203F78"), TextAlignment.Center);
            label.Width = 100;
            canvas.Children.Add(At(label, x - 4, 150));
        }
        // To: bar.
        var to = new TextBlock { FontFamily = Tahoma, FontSize = 21, Foreground = Brush(C("#1C2A44")), TextTrimming = TextTrimming.CharacterEllipsis, Width = w - 110 };
        to.Inlines.Add(new System.Windows.Documents.Run("To: ") { FontWeight = FontWeights.Bold });
        to.Inlines.Add(new System.Windows.Documents.Run(who + " "));
        to.Inlines.Add(new System.Windows.Documents.Run("<" + new string(who.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()) + "@hotmail.com>") { Foreground = Brush(C("#7A8496")) });
        to.Inlines.Add(new System.Windows.Documents.Run("  (" + d.Text("status") + ")") { Foreground = Brush(C("#3B8A2E")) });
        canvas.Children.Add(At(new Border { Width = w - 60, Height = 52, CornerRadius = new CornerRadius(6), Background = Brush(Colors.White),
            BorderBrush = Brush(C("#A9BEDF")), BorderThickness = new Thickness(1.5), Padding = new Thickness(16, 12, 16, 0), Child = to }, 30, 194));

        // Conversation.
        const double convX = 30, convY = 262, convW = 650, convH = 650;
        canvas.Children.Add(At(new Border { Width = convW, Height = convH, Background = Brush(Colors.White), BorderBrush = Brush(C("#A9BEDF")), BorderThickness = new Thickness(1.5) }, convX, convY));
        var says = Label(who + " says:", Tahoma, 20, FontWeights.Normal, C("#6B6B6B"), TextAlignment.Left);
        canvas.Children.Add(At(says, convX + 22, convY + 22));
        var lines = LyricLines(d.LyricText, face?.Family ?? Trebuchet, face?.Weight ?? FontWeights.Normal, C("#1B3FA0"), convW - 64, convH - 230,
            Presence(d, 58), 24, d.Align(TextAlignment.Left), Leading(d, 1.2), gap: 8, wholeShrink: .72);
        canvas.Children.Add(At(lines, convX + 40, convY + 60));
        lines.Measure(new Size(convW - 64, double.PositiveInfinity));
        var replyY = convY + 60 + lines.DesiredSize.Height + 34;
        canvas.Children.Add(At(Label("You say:", Tahoma, 20, FontWeights.Normal, C("#6B6B6B"), TextAlignment.Left), convX + 22, replyY));
        var reply = new StackPanel { Orientation = Orientation.Horizontal };
        reply.Children.Add(Emoticon(34, heart: true));
        reply.Children.Add(Label("  on repeat", Trebuchet, 28, FontWeights.Normal, C("#A3236B"), TextAlignment.Left));
        canvas.Children.Add(At(reply, convX + 40, replyY + 34));

        // Display pictures and the "listening to" message.
        canvas.Children.Add(At(DisplayPicture(d, 200, available: true), 712, 262));
        var listening = new TextBlock { Width = 210, TextWrapping = TextWrapping.Wrap, FontFamily = Tahoma, FontSize = 18, Foreground = Brush(C("#4E5A70")) };
        listening.Inlines.Add(new System.Windows.Documents.Run("♫ ") { Foreground = Brush(C("#2F6FD0")) });
        listening.Inlines.Add(new System.Windows.Documents.Run(d.Title + " - " + d.Artist) { FontStyle = FontStyles.Italic });
        canvas.Children.Add(At(listening, 712, 490));
        canvas.Children.Add(At(DisplayPicture(null, 150, available: true), 737, 760));

        // Formatting bar and typing box.
        var format = new StackPanel { Orientation = Orientation.Horizontal };
        format.Children.Add(Label("A", Georgia, 28, FontWeights.Bold, C("#1C2A44"), TextAlignment.Left));
        format.Children.Add(new Border { Width = 18 });
        format.Children.Add(Emoticon(30));
        format.Children.Add(new Border { Width = 14 });
        format.Children.Add(Emoticon(30, wink: true));
        format.Children.Add(new Border { Width = 14 });
        format.Children.Add(Glyph("", 26, blue, HorizontalAlignment.Left));
        format.Children.Add(new Border { Width = 14 });
        format.Children.Add(Label("≈", Tahoma, 30, FontWeights.Bold, C("#C04A2A"), TextAlignment.Left));
        canvas.Children.Add(At(format, 40, 930));
        var typed = new TextBlock { FontFamily = Trebuchet, FontSize = 26, Foreground = Brush(C("#7A8496")), Text = d.Text("note"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 560 };
        canvas.Children.Add(At(new Border { Width = 650, Height = 150, Background = Brush(Colors.White), BorderBrush = Brush(C("#A9BEDF")), BorderThickness = new Thickness(1.5),
            Padding = new Thickness(18, 16, 18, 0), Child = typed }, convX, 978));
        canvas.Children.Add(At(new Border { Width = 2, Height = 30, Background = Brush(C("#1C2A44")) }, convX + 22 + MeasureWidth(d.Text("note"), Trebuchet, 26), 996));
        foreach (var (name, y) in new[] { ("Send", 978.0), ("Search", 1058.0) })
        {
            var button = new Border { Width = 200, Height = 66, CornerRadius = new CornerRadius(8), BorderBrush = Brush(C("#7D96C2")), BorderThickness = new Thickness(1.5),
                Background = new LinearGradientBrush(C("#FFFFFF"), C("#D4E1F4"), 90), Child = Label(name, Tahoma, 22, FontWeights.Bold, C("#1C2A44"), TextAlignment.Center) };
            ((FrameworkElement)button.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(button, 712, y));
        }
        canvas.Children.Add(At(Label("Last message received at " + d.Text("time") + " on " + DateTime.Today.ToString("d", CultureInfo.CurrentCulture) + ".",
            Tahoma, 18, FontWeights.Normal, C("#55627A"), TextAlignment.Left), 30, 1150));
        canvas.Children.Add(At(Label("♪ Taskitify Messenger", Tahoma, 18, FontWeights.Bold, C("#2F6FD0"), TextAlignment.Right), w - 330, 1150));
        ((TextBlock)canvas.Children[^1]).Width = 300;

        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 50, ShadowDepth = 18, Direction = 290, Opacity = .45 };
        return ObjectStage(d, canvas, w, h, Bliss(), Colors.White, C("#2A5C2A"));
    }

    /// <summary>The rolling green hill under a blue sky that every XP desktop started with.</summary>
    private static Brush Bliss()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new LinearGradientBrush(C("#2C6FD6"), C("#A8D2F4"), 90), null, new RectangleGeometry(new Rect(0, 0, 1000, 1000))));
        g.Children.Add(new GeometryDrawing(new RadialGradientBrush(Alpha(Colors.White, 150), Alpha(Colors.White, 0)), null, new EllipseGeometry(new Point(300, 260), 260, 70)));
        g.Children.Add(new GeometryDrawing(new RadialGradientBrush(Alpha(Colors.White, 120), Alpha(Colors.White, 0)), null, new EllipseGeometry(new Point(760, 180), 200, 50)));
        g.Children.Add(new GeometryDrawing(new LinearGradientBrush(C("#7CC23A"), C("#2E7A12"), 90), null, Geometry.Parse("M0,620 C250,500 620,520 1000,640 L1000,1000 L0,1000 Z")));
        g.Children.Add(new GeometryDrawing(new LinearGradientBrush(Alpha(C("#C8F06A"), 90), Alpha(C("#C8F06A"), 0), 90), null, Geometry.Parse("M0,640 C260,530 600,545 1000,660 L1000,720 C640,620 300,600 0,700 Z")));
        var brush = new DrawingBrush(g) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        return brush;
    }

    private static FrameworkElement Butterfly(double size)
    {
        var c = new Canvas { Width = 40, Height = 40 };
        foreach (var (x, y, w, h, color, angle) in new[]
        {
            (2.0, 4.0, 18.0, 16.0, "#3D8B2F", -20.0), (20.0, 4.0, 18.0, 16.0, "#E8792B", 20.0),
            (5.0, 20.0, 14.0, 14.0, "#2A63C6", 20.0), (21.0, 20.0, 14.0, 14.0, "#D0332D", -20.0),
        })
            c.Children.Add(At(new Ellipse { Width = w, Height = h, Fill = Brush(C(color)), Stroke = Brush(Colors.White), StrokeThickness = 1.2,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(angle) }, x, y));
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    /// <summary>A round yellow smiley (or wink, or red heart), drawn like the classic emoticons.</summary>
    private static FrameworkElement Emoticon(double size, bool wink = false, bool heart = false)
    {
        var c = new Canvas { Width = 32, Height = 32 };
        if (heart)
        {
            c.Children.Add(new Path { Data = Geometry.Parse("M16,29 C6,21 1,15 3,9 C5,3 12,3 16,9 C20,3 27,3 29,9 C31,15 26,21 16,29 Z"),
                Fill = new LinearGradientBrush(C("#FF6A7E"), C("#C3142E"), 90), Stroke = Brush(C("#8E0A1E")), StrokeThickness = 1.2 });
            c.Children.Add(At(new Ellipse { Width = 7, Height = 5, Fill = Brush(Alpha(Colors.White, 170)) }, 8, 8));
        }
        else
        {
            c.Children.Add(new Ellipse { Width = 32, Height = 32, Fill = new RadialGradientBrush(C("#FFF27A"), C("#F2B40C")) { GradientOrigin = new Point(.35, .3) }, Stroke = Brush(C("#B07A00")), StrokeThickness = 1.2 });
            c.Children.Add(At(new Ellipse { Width = 4, Height = 7, Fill = Brush(C("#3A2600")) }, 10, 9));
            if (wink)
                c.Children.Add(new Path { Data = Geometry.Parse("M18,13 L24,12"), Stroke = Brush(C("#3A2600")), StrokeThickness = 2, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round });
            else
                c.Children.Add(At(new Ellipse { Width = 4, Height = 7, Fill = Brush(C("#3A2600")) }, 18, 9));
            c.Children.Add(new Path { Data = Geometry.Parse("M8,19 C11,25 21,25 24,19"), Stroke = Brush(C("#3A2600")), StrokeThickness = 2, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round });
        }
        return new Viewbox { Width = size, Height = size, Child = c, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A display picture in its glossy frame, green while available.</summary>
    private static FrameworkElement DisplayPicture(Studio? d, double size, bool available)
    {
        var picture = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true };
        if (d?.Photo(size - 28, 6, size - 28) is { } photo)
            picture.Child = photo;
        else
            picture.Background = new LinearGradientBrush(C("#7FB2F0"), C("#2A5BC2"), 45);
        if (picture.Child is null)
        {
            var note = Label("♫", Symbols, size * .42, FontWeights.Normal, Colors.White, TextAlignment.Center);
            note.VerticalAlignment = VerticalAlignment.Center;
            picture.Child = note;
        }
        picture.Width = picture.Height = size - 28;
        var frame = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(16), Padding = new Thickness(12),
            Background = available ? new LinearGradientBrush(C("#B9F07A"), C("#3F9E1E"), 90) : new LinearGradientBrush(C("#E2E2E2"), C("#9A9A9A"), 90),
            BorderBrush = Brush(Alpha(Colors.White, 200)), BorderThickness = new Thickness(2), Child = picture,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = .3 },
        };
        return frame;
    }

    // ======================= Nintendo DS Lite =======================

    /// <summary>
    /// An open DS Lite: the top lid with its black-bezelled screen (cover, song, progress) and
    /// speaker grilles, the hinge with its lights, the lower half with the touch screen showing
    /// the lines as a role-playing dialogue, the cross pad, the X/A/B/Y diamond, start and select.
    /// </summary>
    private static FrameworkElement DsLite(Studio d)
    {
        const double w = 900, h = 1150;
        var shell = d.Color("device", C("#EEF0F3"));
        var dark = C("#16181D");
        var ink = C("#22262E");
        var face = d.UserFace;
        var canvas = new Canvas { Width = w, Height = h };
        Brush Gloss(Color c) => new LinearGradientBrush(new GradientStopCollection { new(Lighten(c, .5), 0), new(c, .3), new(Darken(c, .88), 1) }, 90);

        // Top lid.
        canvas.Children.Add(At(new Border { Width = w, Height = 540, CornerRadius = new CornerRadius(46, 46, 22, 22), Background = Gloss(shell),
            BorderBrush = Brush(Darken(shell, .8)), BorderThickness = new Thickness(2) }, 0, 0));
        canvas.Children.Add(At(new Border { Width = 560, Height = 440, CornerRadius = new CornerRadius(14), Background = new LinearGradientBrush(C("#2A2D34"), dark, 90) }, 170, 46));
        var top = new Grid { Width = 480, Height = 360, Background = new LinearGradientBrush(C("#20345C"), C("#0E1A33"), 90), ClipToBounds = true };
        var topCopy = new StackPanel { Margin = new Thickness(26, 22, 26, 0) };
        topCopy.Children.Add(Label("▶ NOW PLAYING", Vt323, 28, FontWeights.Normal, C("#7FE3D8"), TextAlignment.Left));
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = d.Photo(150, 4, 150) ?? new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(d.Accent, Darken(d.Accent, .5), 45) };
        cover.Margin = new Thickness(0, 0, 18, 0);
        row.Children.Add(cover);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = Label(d.Title, Vt323, 40, FontWeights.Normal, C("#F2F6FF"), TextAlignment.Left);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        names.Children.Add(t);
        names.Children.Add(Label(d.Artist, Vt323, 30, FontWeights.Normal, C("#9FB3D9"), TextAlignment.Left));
        Grid.SetColumn(names, 1);
        row.Children.Add(names);
        topCopy.Children.Add(row);
        topCopy.Children.Add(Space(d.Progress(C("#7FE3D8"), C("#2F4670"), 8), 26));
        topCopy.Children.Add(Label((d.Timestamp ? d.Time : "0:00") + " / " + d.Duration, Vt323, 26, FontWeights.Normal, C("#9FB3D9"), TextAlignment.Right));
        top.Children.Add(topCopy);
        top.Children.Add(new Border { Background = DotMatrix(Colors.Black) , Opacity = .5 });
        canvas.Children.Add(At(top, 210, 86));
        foreach (var sx in new[] { 64.0, 768.0 })
            for (var r = 0; r < 4; r++)
                for (var cc = 0; cc < 3; cc++)
                    canvas.Children.Add(At(new Ellipse { Width = 11, Height = 11, Fill = Brush(Darken(shell, .55)) }, sx + (cc * 22), 220 + (r * 22)));
        // Hinge.
        canvas.Children.Add(At(new Border { Width = w - 120, Height = 64, CornerRadius = new CornerRadius(32),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Darken(shell, .72), 0), new(Lighten(shell, .4), .45), new(Darken(shell, .7), 1) }, 90) }, 60, 540));
        canvas.Children.Add(At(new Ellipse { Width = 12, Height = 12, Fill = Brush(C("#43D35A")), Effect = new DropShadowEffect { Color = C("#43D35A"), BlurRadius = 10, ShadowDepth = 0 } }, 700, 566));
        canvas.Children.Add(At(new Ellipse { Width = 12, Height = 12, Fill = Brush(Darken(shell, .6)) }, 724, 566));
        // Lower half.
        canvas.Children.Add(At(new Border { Width = w, Height = 540, CornerRadius = new CornerRadius(22, 22, 46, 46), Background = Gloss(shell),
            BorderBrush = Brush(Darken(shell, .8)), BorderThickness = new Thickness(2) }, 0, 610));
        canvas.Children.Add(At(new Border { Width = 520, Height = 400, CornerRadius = new CornerRadius(8), Background = Brush(Darken(shell, .72)) }, 190, 650));
        var touch = new Grid { Width = 480, Height = 360, Background = new LinearGradientBrush(C("#F5F1E4"), C("#E3DCC6"), 90), ClipToBounds = true };
        var box = new Border { Margin = new Thickness(14), CornerRadius = new CornerRadius(10), BorderBrush = Brush(C("#3A4A66")), BorderThickness = new Thickness(4),
            Background = Brush(C("#FBF8EE")), Padding = new Thickness(18, 12, 18, 10) };
        var dialog = new DockPanel();
        var speaker = new Border { CornerRadius = new CornerRadius(6), Background = Brush(C("#3A4A66")), Padding = new Thickness(12, 2, 12, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8), Child = Label(d.Text("intro"), Vt323, 24, FontWeights.Normal, C("#F5F1E4"), TextAlignment.Left) };
        DockPanel.SetDock(speaker, Dock.Top);
        dialog.Children.Add(speaker);
        var arrow = Label("▼", Symbols, 20, FontWeights.Normal, C("#3A4A66"), TextAlignment.Right);
        DockPanel.SetDock(arrow, Dock.Bottom);
        dialog.Children.Add(arrow);
        dialog.Children.Add(LyricLines(d.LyricText, face?.Family ?? Vt323, face?.Weight ?? FontWeights.Normal, ink, 390, 220, Presence(d, 52), 26,
            d.Align(TextAlignment.Left), Leading(d, 1.0), gap: 4, wholeShrink: .6));
        box.Child = dialog;
        touch.Children.Add(box);
        canvas.Children.Add(At(touch, 210, 670));
        // Controls.
        var pad = new Grid { Width = 150, Height = 150 };
        foreach (var (pw, ph) in new[] { (50.0, 150.0), (150.0, 50.0) })
            pad.Children.Add(new Border { Width = pw, Height = ph, CornerRadius = new CornerRadius(7), Background = new LinearGradientBrush(C("#F7F7F8"), C("#BDBFC4"), 90),
                BorderBrush = Brush(C("#9EA1A7")), BorderThickness = new Thickness(1.5) });
        canvas.Children.Add(At(pad, 22, 720));
        foreach (var (letter, x, y) in new[] { ("X", 785.0, 680.0), ("A", 840.0, 735.0), ("B", 785.0, 790.0), ("Y", 730.0, 735.0) })
        {
            var key = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(25), Background = new LinearGradientBrush(C("#FFFFFF"), C("#C8CACE"), 90),
                BorderBrush = Brush(C("#9EA1A7")), BorderThickness = new Thickness(1.5), Child = Label(letter, Gill, 24, FontWeights.SemiBold, C("#8E939C"), TextAlignment.Center) };
            ((FrameworkElement)key.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(key, x - 50, y));
        }
        foreach (var (name, y) in new[] { ("START", 900.0), ("SELECT", 960.0) })
        {
            canvas.Children.Add(At(new Ellipse { Width = 30, Height = 30, Fill = new LinearGradientBrush(C("#FFFFFF"), C("#C8CACE"), 90), Stroke = Brush(C("#9EA1A7")) }, 760, y));
            canvas.Children.Add(At(Label(name, Gill, 15, FontWeights.SemiBold, Darken(shell, .55), TextAlignment.Left), 798, y + 6));
        }
        canvas.Children.Add(At(new Ellipse { Width = 8, Height = 8, Fill = Brush(Darken(shell, .45)) }, 446, 1080));
        var engraving = Label(d.Text("note").ToUpperInvariant(), Gill, 20, FontWeights.SemiBold, Darken(shell, .62), TextAlignment.Center);
        engraving.Width = 400;
        canvas.Children.Add(At(engraving, 250, 1090));
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 22, Direction = 285, Opacity = .45 };
        return ObjectStage(d, canvas, w, h, Desk(C("#8FA3C8"), C("#2C3552")), Colors.White, C("#2C3552"));
    }

    // ======================= Cassette =======================

    /// <summary>
    /// A mixtape: the cassette in smoked plastic (screws, reels and brown tape behind the window,
    /// the label written by hand), its J-card with the lines written out in pen and the
    /// dedication, and the pencil everybody used to wind the tape.
    /// </summary>
    private static FrameworkElement Mixtape(Studio d)
    {
        const double w = 1000, h = 1560;
        var label = d.Color("label", C("#E9C46A"));
        var pen = C("#1E2B57");
        var face = d.UserFace;
        var canvas = new Canvas { Width = w, Height = h };

        // The J-card, under the tape.
        var card = new Grid { Width = 860, Height = 880, Background = Fibers(C("#F6F0E2"), Alpha(C("#C2B391"), 30)) };
        card.Children.Add(new Border { Height = 92, VerticalAlignment = VerticalAlignment.Top, Background = new LinearGradientBrush(Lighten(label, .1), Darken(label, .85), 0) });
        card.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(110, 92, 0, 0), Background = Brush(Alpha(C("#5A4A2E"), 60)) });
        var rules = new DrawingGroup();
        rules.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 10, 64))));
        rules.Children.Add(new GeometryDrawing(Brush(Alpha(C("#7E9DC4"), 90)), null, new RectangleGeometry(new Rect(0, 62.6, 10, 1.4))));
        card.Children.Add(new Border { Margin = new Thickness(130, 170, 40, 60), Background = new DrawingBrush(rules) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 64), ViewportUnits = BrushMappingMode.Absolute } });
        var cardText = new Canvas();
        cardText.Children.Add(At(Spaced("SIDE " + d.Text("side"), Figtree, 30, FontWeights.Black, Colors.White, TextAlignment.Left, .3), 36, 26));
        cardText.Children.Add(At(Handwriting(d.Text("title"), HandFont, 60, pen, 640, 51, TextAlignment.Left, 64), 140, 92));
        cardText.Children.Add(At(Handwriting(Presence(d, 1) >= 1.1 ? "♪" : "♪", HandFont, 46, pen, 60, 52, TextAlignment.Left, 64), 48, 186));
        var body = Handwriting(d.LyricText, face?.Family ?? HandFont, Presence(d, 66), pen, 680, 53, d.Align(TextAlignment.Left), 64,
            maxHeight: 64 * 9, weight: face?.Weight ?? FontWeights.SemiBold, preferWhole: d.LyricText.Length <= 110);
        cardText.Children.Add(At(body, 140, 178));
        var dedication = Handwriting(d.Text("to") + " →", HandFont, 46, Alpha(C("#9C2E3A"), 235), 520, 54, TextAlignment.Right, 60);
        cardText.Children.Add(At(dedication, 300, 790));
        card.Children.Add(cardText);
        card.RenderTransformOrigin = new Point(.5, .5);
        card.RenderTransform = new RotateTransform(2.2);
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 14, Direction = 280, Opacity = .35 };
        canvas.Children.Add(At(card, 70, 640));

        // The cassette.
        var tape = new Canvas { Width = 1000, Height = 630 };
        var body2 = RoundRect(0, 0, 1000, 630, 34, 34, 34, 34);
        tape.Children.Add(new Path { Data = body2, Fill = new LinearGradientBrush(new GradientStopCollection { new(C("#3A3B42"), 0), new(C("#1F2026"), .6), new(C("#121318"), 1) }, 90),
            Stroke = Brush(C("#0B0B0E")), StrokeThickness = 3 });
        tape.Children.Add(new Path { Data = body2, Fill = new LinearGradientBrush(Alpha(Colors.White, 34), Alpha(Colors.White, 0), new Point(0, 0), new Point(.4, .6)) });
        foreach (var (x, y) in new[] { (26.0, 26.0), (954.0, 26.0), (26.0, 584.0), (954.0, 584.0), (490.0, 584.0) })
            tape.Children.Add(At(Screw(C("#9A9AA2"), 24), x - 2, y - 2));
        // Label sticker.
        var sticker = new Grid { Width = 880, Height = 380, Background = Fibers(C("#F8F2E2"), Alpha(C("#C2B391"), 26)) };
        sticker.Children.Add(new Border { Height = 70, VerticalAlignment = VerticalAlignment.Top, Background = new LinearGradientBrush(Lighten(label, .12), label, 0) });
        sticker.Children.Add(new Border { Height = 56, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(label, Darken(label, .8), 0) });
        foreach (var y in new[] { 112.0, 164.0 })
            sticker.Children.Add(new Border { Height = 1.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(130, y, 40, 0), Background = Brush(Alpha(C("#5A4A2E"), 80)) });
        var side = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(8), Background = Brush(C("#1E2026")), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, 84, 0, 0), Child = Label(d.Text("side"), Figtree, 54, FontWeights.Black, label, TextAlignment.Center) };
        ((FrameworkElement)side.Child).VerticalAlignment = VerticalAlignment.Center;
        sticker.Children.Add(side);
        var stickerText = new Canvas();
        stickerText.Children.Add(At(Handwriting(d.Text("title"), HandFont, 50, pen, 600, 61, TextAlignment.Left, 52), 136, 64));
        stickerText.Children.Add(At(Handwriting(d.Artist + " • " + d.Title, HandFont, 34, Alpha(pen, 210), 600, 62, TextAlignment.Left, 52), 140, 118));
        stickerText.Children.Add(At(Label("C-60", Figtree, 40, FontWeights.Black, C("#1E2026"), TextAlignment.Right), 740, 14));
        sticker.Children.Add(stickerText);
        tape.Children.Add(At(sticker, 60, 40));
        // Window with reels and tape.
        tape.Children.Add(At(new Border { Width = 470, Height = 140, CornerRadius = new CornerRadius(70), Background = Brush(C("#0D0E12")),
            BorderBrush = Brush(C("#2C2D34")), BorderThickness = new Thickness(4) }, 265, 230));
        tape.Children.Add(At(new Ellipse { Width = 210, Height = 210, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")) }, 222, 196));
        tape.Children.Add(At(new Ellipse { Width = 150, Height = 150, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")) }, 625, 226));
        foreach (var cx in new[] { 327.0, 700.0 })
        {
            tape.Children.Add(At(new Ellipse { Width = 86, Height = 86, Fill = Brush(C("#E8E6E0")), Stroke = Brush(C("#9A9AA2")), StrokeThickness = 3 }, cx - 43, 257));
            for (var k = 0; k < 6; k++)
            {
                var angle = k * Math.PI / 3;
                tape.Children.Add(At(new Rectangle { Width = 10, Height = 18, Fill = Brush(C("#9A9AA2")), RenderTransformOrigin = new Point(.5, .5),
                    RenderTransform = new RotateTransform(k * 60) }, cx - 5 + (Math.Sin(angle) * 22), 291 - (Math.Cos(angle) * 22)));
            }
        }
        // The lower trapezoid with its holes and the tape running across.
        tape.Children.Add(new Path { Data = Geometry.Parse("M190,630 L250,450 L750,450 L810,630 Z"), Fill = new LinearGradientBrush(C("#2C2D34"), C("#17181D"), 90), Stroke = Brush(C("#0B0B0E")), StrokeThickness = 2 });
        tape.Children.Add(At(new Rectangle { Width = 600, Height = 10, Fill = Brush(C("#4A3220")) }, 200, 600));
        foreach (var x in new[] { 300.0, 400.0, 576.0, 676.0 })
            tape.Children.Add(At(new Ellipse { Width = 28, Height = 28, Fill = Brush(C("#08080A")) }, x, 520));
        tape.Children.Add(At(Label(d.Text("note"), Gill, 17, FontWeights.SemiBold, C("#9A9AA2"), TextAlignment.Center), 300, 470));
        ((TextBlock)tape.Children[^1]).Width = 400;
        tape.RenderTransformOrigin = new Point(.5, .5);
        tape.RenderTransform = new RotateTransform(-4.5);
        tape.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 50, ShadowDepth = 20, Direction = 285, Opacity = .5 };
        canvas.Children.Add(At(tape, 0, 30));

        // The pencil.
        var pencil = new Canvas { Width = 640, Height = 40 };
        pencil.Children.Add(new Path { Data = Geometry.Parse("M60,4 L560,4 L560,36 L60,36 Z"), Fill = new LinearGradientBrush(C("#F7C948"), C("#D99A14"), 90) });
        pencil.Children.Add(new Path { Data = Geometry.Parse("M60,4 L0,20 L60,36 Z"), Fill = Brush(C("#E8C99A")) });
        pencil.Children.Add(new Path { Data = Geometry.Parse("M18,15 L0,20 L18,25 Z"), Fill = Brush(C("#3A3A3A")) });
        pencil.Children.Add(new Path { Data = Geometry.Parse("M560,2 L600,2 L600,38 L560,38 Z"), Fill = new LinearGradientBrush(C("#C9CCD2"), C("#8A8E96"), 90) });
        pencil.Children.Add(new Path { Data = Geometry.Parse("M600,4 L636,4 C640,12 640,28 636,36 L600,36 Z"), Fill = Brush(C("#E77F86")) });
        pencil.RenderTransformOrigin = new Point(.5, .5);
        pencil.RenderTransform = new RotateTransform(-14);
        pencil.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 8, Direction = 290, Opacity = .4 };
        canvas.Children.Add(At(pencil, -60, 1470));

        return ObjectStage(d, canvas, w, h, Desk(C("#5D6B6E"), C("#22292B")), Colors.White, C("#22292B"));
    }

    // ======================= Receipt =======================

    /// <summary>
    /// A thermal receipt: a narrow strip torn at both ends, the shop's double-height name, the
    /// header in reverse print, dotted rules, the lines printed double size as the one item
    /// bought, the totals, the payment, a barcode and the closing message; the print fades
    /// toward the end and the strip curls a little.
    /// </summary>
    private static FrameworkElement ThermalReceipt(Studio d)
    {
        const double w = 620;
        var ink = C("#24242A");
        var face = d.UserFace;
        var stack = new StackPanel { Width = w - 90, Margin = new Thickness(45, 70, 45, 70) };
        FrameworkElement Centre(string text, double size, bool bold = false, int seed = 0)
        {
            var t = Thermal(text, size, ink, seed, bold);
            ((TextBlock)t).TextAlignment = TextAlignment.Center;
            ((TextBlock)t).Width = w - 90;
            return t;
        }
        FrameworkElement Pair2(string left, string right, double size = 24, bool bold = false, int seed = 0)
        {
            var g = new Grid { Width = w - 90, Margin = new Thickness(0, 4, 0, 0) };
            g.Children.Add(Thermal(left, size, ink, seed, bold));
            var r = (TextBlock)Thermal(right, size, ink, seed + 1, bold);
            r.TextAlignment = TextAlignment.Right;
            r.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(r);
            return g;
        }
        FrameworkElement Dots() => Space(Thermal(new string('-', 32), 24, Alpha(ink, 200), 9, false), 10, 10);

        var logo = new Border { Width = 86, Height = 86, CornerRadius = new CornerRadius(43), BorderBrush = Brush(ink), BorderThickness = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center, Child = Label("♫", Symbols, 44, FontWeights.Normal, ink, TextAlignment.Center), OpacityMask = Speckle(71, .7) };
        ((FrameworkElement)logo.Child).VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(logo);
        var shop = (TextBlock)Centre(d.Text("shop").ToUpperInvariant(), 34, true, 1);
        shop.RenderTransform = new ScaleTransform(1, 1.5, 0, 0);
        shop.Margin = new Thickness(0, 18, 0, 22);
        stack.Children.Add(shop);
        stack.Children.Add(Centre("AV. DAS MEMORIAS, 27 - LOJA 4", 20, false, 2));
        stack.Children.Add(Centre("TEL (00) 0027-0027", 20, false, 3));
        stack.Children.Add(Dots());
        var header = new Border { Background = Brush(Alpha(ink, 235)), Padding = new Thickness(0, 6, 0, 8), OpacityMask = Speckle(72, .75),
            Child = Label(d.Text("title").ToUpperInvariant(), Courier, 26, FontWeights.Bold, C("#F6F4EE"), TextAlignment.Center) };
        stack.Children.Add(header);
        stack.Children.Add(Space(Pair2("RECEIPT #" + d.Text("id"), d.Text("date").ToUpperInvariant(), 21, false, 4), 14));
        stack.Children.Add(Pair2("CASHIER: TASKITIFY", d.Time, 21, false, 6));
        stack.Children.Add(Dots());
        stack.Children.Add(Pair2("QTY  ITEM", "PRICE", 21, true, 8));
        stack.Children.Add(Space(Thermal("1 x", 22, ink, 10, true), 10));
        // The item: the lines, printed double size.
        var lyric = face is null
            ? LyricLines(d.LyricText, Courier, FontWeights.Bold, Alpha(ink, 228), w - 90, 620, Presence(d, 46), 22, d.Align(TextAlignment.Left), Leading(d, 1.12), gap: 6, wholeShrink: .7)
            : LyricLines(d.LyricText, face.Value.Family, face.Value.Weight, ink, w - 90, 620, Presence(d, 46), 22, d.Align(TextAlignment.Left), Leading(d, 1.14), gap: 6, wholeShrink: .7);
        if (face is null)
            lyric.OpacityMask = Speckle(5, .8);
        lyric.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(lyric);
        stack.Children.Add(Thermal("   " + d.Artist.ToUpperInvariant() + " / " + d.Duration, 19, Alpha(ink, 210), 11, false));
        stack.Children.Add(Pair2("", "♥ 1,00", 24, true, 12));
        stack.Children.Add(Dots());
        stack.Children.Add(Pair2("SUBTOTAL", "1 SONG", 22, false, 14));
        stack.Children.Add(Pair2("TAX", "0 TEARS", 22, false, 16));
        var total = (FrameworkElement)Pair2("TOTAL", "♥ ∞", 34, true, 18);
        total.Margin = new Thickness(0, 12, 0, 4);
        stack.Children.Add(total);
        stack.Children.Add(Thermal(d.Text("extra").ToUpperInvariant(), 20, ink, 20, false));
        stack.Children.Add(Thermal("CHANGE: NONE NEEDED", 20, ink, 21, false));
        stack.Children.Add(Dots());
        var code = LinearBarcode(ink, w - 140, 84);
        code.HorizontalAlignment = HorizontalAlignment.Center;
        code.Margin = new Thickness(0, 10, 0, 6);
        stack.Children.Add(code);
        stack.Children.Add(Centre("0027 " + d.Text("id") + " 4815 1623", 18, false, 22));
        stack.Children.Add(Space(Centre(d.Text("note").ToUpperInvariant(), 24, true, 23), 22));
        stack.Children.Add(Space(Centre("* * *  CUSTOMER COPY  * * *", 19, false, 24), 12));
        stack.Measure(new Size(w, double.PositiveInfinity));
        var h = stack.DesiredSize.Height + 140;

        // The strip: torn edges, a fade toward the end, a fold.
        var edge = new PointCollection();
        for (var x = 0.0; x <= w; x += 20)
            edge.Add(new Point(x, (x / 20) % 2 == 0 ? 0 : 12));
        for (var x = w; x >= 0; x -= 20)
            edge.Add(new Point(x, h - ((x / 20) % 2 == 0 ? 0 : 12)));
        var paper = new Grid { Width = w, Height = h, Clip = new PathGeometry(new[] { new PathFigure(edge[0], edge.Skip(1).Select(p => (PathSegment)new LineSegment(p, false)), true) }) };
        paper.Children.Add(new Border { Background = Fibers(C("#F8F7F2"), Alpha(C("#C9C6BC"), 26)) });
        paper.Children.Add(stack);
        paper.Children.Add(new Border { Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(C("#F8F7F2"), 0), .55), new(Alpha(C("#F8F7F2"), 70), 1) }, 90), IsHitTestVisible = false });
        paper.Children.Add(new Border { Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * .42, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, 20), .45), new(Alpha(Colors.White, 140), .55), new(Alpha(Colors.White, 0), 1) }, 90) });
        paper.Children.Add(new Border { Background = new LinearGradientBrush(Alpha(Colors.Black, 0), Alpha(Colors.Black, 26), 0), Width = 60, HorizontalAlignment = HorizontalAlignment.Right });
        paper.RenderTransformOrigin = new Point(.5, .5);
        paper.RenderTransform = new RotateTransform(-1.6);
        paper.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 18, Direction = 285, Opacity = .45 };
        var holder = new Canvas { Width = w + 80, Height = h + 60 };
        holder.Children.Add(At(paper, 40, 30));
        return ObjectStage(d, holder, holder.Width, holder.Height, Desk(C("#5A4636"), C("#241B15")), C("#EFE6DA"), C("#241B15"));
    }

    // ======================= Fantasy card =======================

    /// <summary>
    /// A collectible card: black border, a coloured frame, the name bar with its cost symbols,
    /// the cover as the art, the type line with the rarity's set symbol, the lines as flavour text
    /// on parchment, collector number and artist credit, and a foil sheen.
    /// </summary>
    private static FrameworkElement FantasyCard2(Studio d)
    {
        const double w = 900, h = 1256;
        var frame = Luminance(d.Accent) > .6 ? Darken(d.Accent, .7) : d.Accent;
        var parchment = C("#EDE3C8");
        var ink = C("#1F1A14");
        var face = d.UserFace;
        var rarity = d.Text("rarity").ToLowerInvariant();
        var gem = rarity.Contains("myth") ? C("#E2632B") : rarity.Contains("rare") ? C("#C9A43A") : rarity.Contains("uncommon") ? C("#A9B4C0") : C("#2A2A2A");
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new Path { Data = RoundRect(0, 0, w, h, 38, 38, 38, 38), Fill = Brush(C("#0C0C0E")) });
        canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 110, CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Lighten(frame, .35), 0), new(frame, .4), new(Darken(frame, .7), 1) }, 45),
            BorderBrush = Brush(Darken(frame, .55)), BorderThickness = new Thickness(3) }, 28, 28));
        canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 110, CornerRadius = new CornerRadius(16), Background = Fibers(Alpha(Colors.White, 0), Alpha(Colors.Black, 30)) }, 28, 28));
        Border Bar(UIElement content, double y, double height) => new()
        {
            Width = w - 110, Height = height, CornerRadius = new CornerRadius(height / 2), Padding = new Thickness(28, 0, 22, 0),
            Background = new LinearGradientBrush(Lighten(frame, .82), Lighten(frame, .55), 90), BorderBrush = Brush(Darken(frame, .45)), BorderThickness = new Thickness(3),
            Child = content, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 3, Opacity = .35 },
        };
        var nameRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var cost = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var symbol in d.Text("symbol").Where(c => !char.IsWhiteSpace(c)).Take(4))
        {
            var pip = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(6, 0, 0, 0),
                Background = new RadialGradientBrush(Lighten(frame, .6), frame), BorderBrush = Brush(C("#1A1A1A")), BorderThickness = new Thickness(2),
                Child = Label(symbol.ToString(), Symbols, 24, FontWeights.Normal, C("#141414"), TextAlignment.Center) };
            ((FrameworkElement)pip.Child).VerticalAlignment = VerticalAlignment.Center;
            cost.Children.Add(pip);
        }
        DockPanel.SetDock(cost, Dock.Right);
        nameRow.Children.Add(cost);
        var name = Label(d.Title, DmSerif, 44, FontWeights.Normal, ink, TextAlignment.Left);
        name.VerticalAlignment = VerticalAlignment.Center;
        nameRow.Children.Add(name);
        canvas.Children.Add(At(Bar(nameRow, 0, 84), 55, 50));
        // Art.
        var art = new Border { Width = w - 130, Height = 500, BorderThickness = new Thickness(6), BorderBrush = new LinearGradientBrush(Darken(frame, .4), Lighten(frame, .5), 45) };
        art.Child = d.Photo(488, 0, w - 142) ?? Artwork(488, d.Accent, 1);
        canvas.Children.Add(At(art, 65, 146));
        // Type line with the set symbol.
        var typeRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var set = new Path { Data = Geometry.Parse("M20,0 L40,14 L32,40 L8,40 L0,14 Z"), Fill = new LinearGradientBrush(Lighten(gem, .45), Darken(gem, .7), 90),
            Stroke = Brush(C("#141414")), StrokeThickness = 2, Width = 40, Height = 40, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(set, Dock.Right);
        typeRow.Children.Add(set);
        var type = Label(d.Text("type") + " — " + d.Text("category"), DmSerif, 36, FontWeights.Normal, ink, TextAlignment.Left);
        type.VerticalAlignment = VerticalAlignment.Center;
        typeRow.Children.Add(type);
        canvas.Children.Add(At(Bar(typeRow, 0, 72), 55, 660));
        // Text box: the lines as flavour text.
        var text = new Grid { Width = w - 130, Height = 400, Background = Fibers(parchment, Alpha(C("#A98F5E"), 40)) };
        text.Children.Add(new Border { BorderBrush = Brush(Darken(frame, .5)), BorderThickness = new Thickness(3) });
        var flavour = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(44, 20, 44, 20) };
        flavour.Children.Add(LyricLines(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, ink, w - 218, 290, Presence(d, 46), 22,
            d.Align(TextAlignment.Left), Leading(d, 1.22), gap: 4, wholeShrink: .78, italic: face is null));
        var credit = Label("— " + d.Artist, Georgia, 26, FontWeights.Normal, Alpha(ink, 200), TextAlignment.Right);
        credit.FontStyle = FontStyles.Italic;
        credit.Margin = new Thickness(0, 14, 0, 0);
        flavour.Children.Add(credit);
        text.Children.Add(flavour);
        canvas.Children.Add(At(text, 65, 746));
        // Collector line.
        canvas.Children.Add(At(Label(d.Text("id") + "  " + char.ToUpperInvariant(rarity.FirstOrDefault('c')) + "\n♪ TSK • EN", Figtree, 18, FontWeights.Bold, C("#EDE6D8"), TextAlignment.Left), 50, 1166));
        canvas.Children.Add(At(Label("✎ Illus. " + d.Artist, Figtree, 20, FontWeights.SemiBold, C("#EDE6D8"), TextAlignment.Center), 260, 1172));
        ((TextBlock)canvas.Children[^1]).Width = 380;
        canvas.Children.Add(At(Label("™ & © Taskitify", Figtree, 17, FontWeights.SemiBold, C("#B8B0A0"), TextAlignment.Right), 640, 1176));
        ((TextBlock)canvas.Children[^1]).Width = 210;
        // Foil.
        canvas.Children.Add(new Path { Data = RoundRect(0, 0, w, h, 38, 38, 38, 38), Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Alpha(C("#FF9AD5"), 0), .2), new(Alpha(C("#9AE6FF"), 40), .38), new(Alpha(C("#FFF3A0"), 34), .5), new(Alpha(C("#B39AFF"), 30), .62), new(Alpha(C("#FFFFFF"), 0), .8),
        }, new Point(0, 0), new Point(1, 1)), IsHitTestVisible = false });
        canvas.RenderTransformOrigin = new Point(.5, .5);
        canvas.RenderTransform = new RotateTransform(-2.5);
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 24, Direction = 285, Opacity = .55 };
        var holder = new Canvas { Width = w + 80, Height = h + 80 };
        holder.Children.Add(At(canvas, 40, 40));
        var playmat = new RadialGradientBrush(Lighten(Darken(d.Accent, .45), .1), C("#0E0C14")) { Center = new Point(.5, .35), GradientOrigin = new Point(.5, .3), RadiusX = .9, RadiusY = .8 };
        return ObjectStage(d, holder, holder.Width, holder.Height, playmat, Colors.White, C("#0E0C14"));
    }

    // ======================= RPG inventory =======================

    /// <summary>
    /// A fantasy game's inventory: a navy panel in a gilded double frame with corner ornaments,
    /// tabs, a grid of item slots (the song glowing in its rarity colour), and the detail pane:
    /// name, rarity, rating, the lines as the item's lore, its stats and the button prompts.
    /// </summary>
    private static FrameworkElement Inventory2(Studio d)
    {
        const double w = 1000, h = 1360;
        var gold = C("#D9B45A");
        var navy = C("#121A36");
        var face = d.UserFace;
        var rarity = d.Text("rarity");
        var glow = rarity.ToLowerInvariant() switch { var r when r.Contains("legend") => C("#F29A2E"), var r when r.Contains("epic") => C("#B266F2"), var r when r.Contains("rare") => C("#4C9AF0"), _ => C("#8FD16A") };
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(new GradientStopCollection { new(C("#1C2850"), 0), new(navy, .5), new(C("#0B1026"), 1) }, 90),
            BorderBrush = new LinearGradientBrush(Lighten(gold, .4), Darken(gold, .6), 45), BorderThickness = new Thickness(8) });
        canvas.Children.Add(At(new Border { Width = w - 40, Height = h - 40, CornerRadius = new CornerRadius(10), BorderBrush = Brush(Alpha(gold, 170)), BorderThickness = new Thickness(2) }, 20, 20));
        foreach (var (x, y, a) in new[] { (20.0, 20.0, 0.0), (w - 20, 20.0, 90.0), (w - 20, h - 20, 180.0), (20.0, h - 20, 270.0) })
            canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,0 L70,0 C50,6 40,14 34,34 C14,40 6,50 0,70 Z M14,14 m-6,0 a6,6 0 1,0 12,0 a6,6 0 1,0 -12,0"),
                Fill = new LinearGradientBrush(Lighten(gold, .45), Darken(gold, .65), 45), RenderTransform = new RotateTransform(a) }, x, y));
        // Title and tabs.
        canvas.Children.Add(At(Spaced("INVENTORY", DmSerif, 46, FontWeights.Normal, gold, TextAlignment.Left, .3), 70, 58));
        canvas.Children.Add(At(Label("♪ 027 G", Vt323, 40, FontWeights.Normal, gold, TextAlignment.Right), w - 300, 64));
        ((TextBlock)canvas.Children[^1]).Width = 230;
        var tabs = new[] { "Items", "Key Items", "Songs" };
        for (var i = 0; i < tabs.Length; i++)
        {
            var active = i == 2;
            canvas.Children.Add(At(new Border { Width = 200, Height = 54, CornerRadius = new CornerRadius(10, 10, 0, 0), Margin = new Thickness(0),
                Background = active ? new LinearGradientBrush(C("#2C3C74"), C("#1C2850"), 90) : Brush(Alpha(Colors.Black, 60)),
                BorderBrush = Brush(Alpha(gold, (byte)(active ? 220 : 90))), BorderThickness = new Thickness(2, 2, 2, 0),
                Child = Label(tabs[i], Georgia, 24, active ? FontWeights.Bold : FontWeights.Normal, active ? C("#F4E7C4") : C("#8A93B8"), TextAlignment.Center) }, 70 + (i * 212), 140));
            ((FrameworkElement)((Border)canvas.Children[^1]).Child).VerticalAlignment = VerticalAlignment.Center;
        }
        canvas.Children.Add(At(new Border { Width = w - 140, Height = 2, Background = Brush(Alpha(gold, 200)) }, 70, 194));
        // Slots.
        string[] icons = ["⚗", "⚔", "✦", "☘", "✉", "♛", "♬", "☕", "❂", "☾"];
        for (var i = 0; i < 10; i++)
        {
            var x = 70 + ((i % 5) * 176);
            var y = 224 + ((i / 5) * 176);
            var selected = i == 6;
            var slot = new Border { Width = 156, Height = 156, CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(Alpha(Colors.White, (byte)(selected ? 40 : 14)), Alpha(Colors.Black, 70), 90),
                BorderBrush = Brush(selected ? glow : Alpha(gold, 90)), BorderThickness = new Thickness(selected ? 4 : 2),
                Child = Label(icons[i], Symbols, 64, FontWeights.Normal, selected ? Lighten(glow, .35) : C("#8A93B8"), TextAlignment.Center) };
            ((FrameworkElement)slot.Child).VerticalAlignment = VerticalAlignment.Center;
            if (selected)
                slot.Effect = new DropShadowEffect { Color = glow, BlurRadius = 30, ShadowDepth = 0, Opacity = .9 };
            canvas.Children.Add(At(slot, x, y));
            if (i is 0 or 3 or 8)
                canvas.Children.Add(At(Label("x" + (i + 2), Vt323, 28, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Right), x + 96, y + 116));
        }
        canvas.Children.Add(At(Label("▼", Symbols, 26, FontWeights.Normal, glow, TextAlignment.Left), 70 + 176 + 64, 560));
        // Detail pane.
        var pane = new Border { Width = w - 140, Height = 640, CornerRadius = new CornerRadius(14), Background = Brush(Alpha(Colors.Black, 80)),
            BorderBrush = Brush(Alpha(gold, 140)), BorderThickness = new Thickness(2), Padding = new Thickness(40, 30, 40, 28) };
        var detail = new StackPanel();
        var title = Label(d.Title, DmSerif, 50, FontWeights.Normal, Lighten(glow, .25), TextAlignment.Left);
        title.Effect = new DropShadowEffect { Color = glow, BlurRadius = 18, ShadowDepth = 0, Opacity = .7 };
        detail.Children.Add(title);
        var meta = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var stars = Label(d.Text("rating"), Symbols, 30, FontWeights.Normal, gold, TextAlignment.Right);
        DockPanel.SetDock(stars, Dock.Right);
        meta.Children.Add(stars);
        meta.Children.Add(Spaced((rarity + " · " + d.Text("item")).ToUpperInvariant(), Figtree, 20, FontWeights.Bold, glow, TextAlignment.Left, .25));
        detail.Children.Add(meta);
        detail.Children.Add(new Border { Height = 1.5, Background = Brush(Alpha(gold, 120)), Margin = new Thickness(0, 18, 0, 22) });
        detail.Children.Add(LyricLines(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, C("#F1E9D2"), w - 220, 250, Presence(d, 48), 24,
            d.Align(TextAlignment.Left), Leading(d, 1.24), gap: 4, wholeShrink: .78, italic: face is null));
        var stats = new Grid { Margin = new Thickness(0, 26, 0, 0) };
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(Label("VALUE      " + d.Text("value"), Vt323, 32, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Left));
        left.Children.Add(Label("TRADEABLE  " + d.Text("trade"), Vt323, 32, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Left));
        stats.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(Label("+99 Nostalgia", Vt323, 32, FontWeights.Normal, C("#7FE07A"), TextAlignment.Left));
        right.Children.Add(Label("+" + (d.Timestamp ? d.Time : "27") + " Replay", Vt323, 32, FontWeights.Normal, C("#7FE07A"), TextAlignment.Left));
        Grid.SetColumn(right, 1);
        stats.Children.Add(right);
        detail.Children.Add(stats);
        var lore = Label("“" + d.Text("note") + "”", Georgia, 24, FontWeights.Normal, C("#9AA3C6"), TextAlignment.Left);
        lore.FontStyle = FontStyles.Italic;
        lore.Margin = new Thickness(0, 18, 0, 0);
        detail.Children.Add(lore);
        pane.Child = detail;
        canvas.Children.Add(At(pane, 70, 600));
        // Prompts.
        var prompts = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, text2, color) in new[] { ("A", "Use", "#5FBF4A"), ("B", "Back", "#D9534F"), ("Y", "Favorite", "#E8B93C") })
        {
            var k = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Brush(C(color)), Margin = new Thickness(0, 0, 10, 0),
                Child = Label(key, Figtree, 22, FontWeights.Black, Colors.White, TextAlignment.Center) };
            ((FrameworkElement)k.Child).VerticalAlignment = VerticalAlignment.Center;
            prompts.Children.Add(k);
            prompts.Children.Add(Label(text2, Vt323, 34, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Left));
            prompts.Children.Add(new Border { Width = 40 });
        }
        canvas.Children.Add(At(prompts, 76, h - 104));
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 20, Direction = 285, Opacity = .5 };
        return ObjectStage(d, canvas, w, h, Desk(C("#3E3A5C"), C("#0D0B18")), C("#E8DDBE"), C("#0D0B18"));
    }

    // ======================= Paint 98 =======================

    /// <summary>
    /// A Paint window from 1998 on the teal desktop: menus, the two-column tool box with its
    /// options well, the lines typed with the text tool (no font smoothing, a dashed text frame
    /// with its handles), doodles drawn with the pencil, the 28-colour palette and the status bar.
    /// </summary>
    private static FrameworkElement Paint98(Studio d)
    {
        const double w = 1000;
        var ink = d.Color("ink", C("#0B24FB"));
        var doodle = d.Color("doodle", C("#FF0080"));
        var face = d.UserFace;
        var body = new Grid { Width = w - 20 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 0, 8) };
        foreach (var item in new[] { "File", "Edit", "View", "Image", "Colors", "Help" })
        {
            var t = new TextBlock { FontFamily = Classic95, FontSize = 24, Margin = new Thickness(0, 0, 30, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run(item[..1]) { TextDecorations = TextDecorations.Underline });
            t.Inlines.Add(new System.Windows.Documents.Run(item[1..]));
            menu.Children.Add(t);
        }
        body.Children.Add(menu);

        var work = new Grid { Height = 820 };
        work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        work.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(work, 1);
        // Tool box.
        var toolbox = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        string[] toolPaths =
        [
            "M4,4 h24 v24 h-24 Z", "M8,4 L24,4 L28,28 L4,28 Z", "M6,24 L20,8 L26,14 L12,28 Z M20,8 L24,4 L28,8 L26,14", "M16,4 L16,20 M10,24 L22,24 L16,30 Z",
            "M12,12 m-8,0 a8,8 0 1,0 16,0 a8,8 0 1,0 -16,0 M18,18 L28,28", "M6,26 L22,10 L26,14 L10,30 Z M22,10 L26,6", "M6,26 C10,18 18,22 26,6", "M8,8 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M16,14 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M10,22 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M22,24 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0",
            "M10,28 L16,4 L22,28 M12,20 L20,20", "M4,28 L28,4", "M4,26 C10,2 22,30 28,6", "M5,8 h22 v16 h-22 Z",
            "M4,26 L10,6 L22,10 L28,26 Z", "M16,16 m-12,0 a12,8 0 1,0 24,0 a12,8 0 1,0 -24,0", "M6,8 h20 a4,4 0 0 1 4,4 v8 a4,4 0 0 1 -4,4 h-20 a4,4 0 0 1 -4,-4 v-8 a4,4 0 0 1 4,-4 Z", "M6,6 L26,26 M26,6 L6,26",
        ];
        for (var i = 0; i < toolPaths.Length; i++)
        {
            var icon = new Path { Data = Geometry.Parse(toolPaths[i]), Stroke = Brush(Colors.Black), StrokeThickness = 2, Width = 32, Height = 32, Stretch = Stretch.None };
            if (i == 8)
                icon.Fill = null;
            if (i == 1 || i == 3)
                icon.Fill = Brush(C("#C0C0C0"));
            RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
            grid.Children.Add(Bevel(new Grid { Width = 34, Height = 34, Children = { icon } }, 1, raised: i != 8, margin: new Thickness(1), padding: 4));
        }
        toolbox.Children.Add(grid);
        var options = Bevel(new StackPanel
        {
            Width = 88, Height = 120,
            Children =
            {
                new Border { Height = 2, Margin = new Thickness(10, 22, 10, 0), Background = Brush(Colors.Black) },
                new Border { Height = 4, Margin = new Thickness(10, 18, 10, 0), Background = Brush(Colors.Black) },
                new Border { Height = 6, Margin = new Thickness(10, 18, 10, 0), Background = Brush(C("#000080")) },
            },
        }, 1, raised: false, margin: new Thickness(4, 10, 4, 0), padding: 0);
        toolbox.Children.Add(options);
        work.Children.Add(toolbox);
        // Canvas well.
        var well = new Grid { Background = Brush(C("#808080")) };
        var sheet = new Grid { Width = 800, Height = 760, Background = Brush(Colors.White), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        RenderOptions.SetEdgeMode(sheet, EdgeMode.Aliased);
        TextOptions.SetTextRenderingMode(sheet, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(sheet, TextFormattingMode.Display);
        var art = new Canvas();
        // Doodles drawn with the pencil.
        var shape = d.Text("shape");
        if (shape == "Stars")
            foreach (var (x, y, s) in new[] { (640.0, 70.0, 1.2), (690.0, 600.0, .9), (70.0, 620.0, .7), (110.0, 70.0, .6) })
                art.Children.Add(PixelStroke(StarPath(x, y, 34 * s), doodle, 5));
        else if (shape == "Hearts")
            foreach (var (x, y, s) in new[] { (640.0, 80.0, 1.2), (700.0, 590.0, .9), (90.0, 640.0, .7) })
                art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M{x},{y + (30 * s)} C{x - (40 * s)},{y} {x - (24 * s)},{y - (30 * s)} {x},{y - (10 * s)} C{x + (24 * s)},{y - (30 * s)} {x + (40 * s)},{y} {x},{y + (30 * s)} Z")), doodle, 5));
        // Spray-can speckles in a corner.
        var spray = new Random(98);
        for (var i = 0; i < 140; i++)
        {
            var a = spray.NextDouble() * Math.PI * 2;
            var r = Math.Sqrt(spray.NextDouble()) * 70;
            art.Children.Add(At(new Rectangle { Width = 3, Height = 3, Fill = Brush(doodle) }, 680 + (Math.Cos(a) * r), 690 + (Math.Sin(a) * r * .7)));
        }
        // The lines, typed with the text tool, in their dashed frame with handles.
        var lyric = LyricLines(d.LyricText, face?.Family ?? Arial, face?.Weight ?? FontWeights.Bold, ink, 600, 430, Presence(d, 58), 26,
            d.Align(TextAlignment.Left), Leading(d, 1.1), gap: 4, wholeShrink: .75);
        lyric.Measure(new Size(600, double.PositiveInfinity));
        var lh = lyric.DesiredSize.Height;
        var top = Math.Max(150, (760 - lh) / 2 - 30);
        art.Children.Add(At(lyric, 100, top));
        art.Children.Add(At(new Rectangle { Width = 640, Height = lh + 40, Stroke = Brush(Colors.Black), StrokeThickness = 1.5, StrokeDashArray = [4, 3] }, 80, top - 20));
        foreach (var (hx, hy) in new[] { (80.0, top - 20), (400.0, top - 20), (720.0, top - 20), (80.0, top + lh + 20), (400.0, top + lh + 20), (720.0, top + lh + 20), (80.0, top + (lh / 2)), (720.0, top + (lh / 2)) })
            art.Children.Add(At(new Rectangle { Width = 9, Height = 9, Fill = Brush(C("#000080")) }, hx - 4, hy - 4));
        if (shape == "Underline")
            art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M110,{top + lh + 50} C260,{top + lh + 70} 480,{top + lh + 36} 690,{top + lh + 58}")), doodle, 6));
        // The signature, typed small at the bottom.
        art.Children.Add(At(Label("- " + d.Title + ", " + d.Artist, Arial, 22, FontWeights.Normal, Darken(ink, .7), TextAlignment.Left), 100, 700));
        sheet.Children.Add(art);
        well.Children.Add(sheet);
        var wellBox = Bevel(well, 1, raised: false, padding: 0);
        Grid.SetColumn(wellBox, 1);
        work.Children.Add(wellBox);
        body.Children.Add(work);
        // Palette.
        string[] palette =
        [
            "#000000", "#808080", "#800000", "#808000", "#008000", "#008080", "#000080", "#800080", "#808040", "#004040", "#0080FF", "#004080", "#4000FF", "#804000",
            "#FFFFFF", "#C0C0C0", "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FFFF80", "#00FF80", "#80FFFF", "#8080FF", "#FF0080", "#FF8040",
        ];
        var paletteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 0, 6) };
        var current = new Grid { Width = 60, Height = 60, Margin = new Thickness(0, 0, 10, 0) };
        current.Children.Add(At(new Border { Width = 34, Height = 34, Background = Brush(Colors.White), BorderBrush = Brush(C("#808080")), BorderThickness = new Thickness(2) }, 22, 22));
        current.Children.Add(new Canvas { Children = { At(new Border { Width = 34, Height = 34, Background = Brush(ink), BorderBrush = Brush(C("#808080")), BorderThickness = new Thickness(2) }, 6, 6) } });
        paletteRow.Children.Add(Bevel(current, 1, raised: false, padding: 0));
        var swatches = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 14 };
        foreach (var hex in palette)
            swatches.Children.Add(Bevel(new Border { Width = 26, Height = 22, Background = Brush(C(hex)) }, 1, raised: false, margin: new Thickness(1), padding: 0));
        paletteRow.Children.Add(swatches);
        Grid.SetRow(paletteRow, 2);
        body.Children.Add(paletteRow);
        var status = new Grid { Margin = new Thickness(4, 4, 4, 2) };
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        status.Children.Add(Bevel(new TextBlock { Text = "For Help, click Help Topics on the Help Menu.", FontFamily = Classic95, FontSize = 20 }, 1, raised: false, padding: 4));
        var coords = Bevel(new TextBlock { Text = "512,384", FontFamily = Classic95, FontSize = 20 }, 1, raised: false, padding: 4);
        Grid.SetColumn(coords, 1);
        status.Children.Add(coords);
        Grid.SetRow(status, 3);
        body.Children.Add(status);

        var window = Window95(d.Text("file") + " - Paint", body, w, 1, active: true);
        window.Measure(new Size(w, double.PositiveInfinity));
        var h = window.DesiredSize.Height;
        var holder = new Canvas { Width = w + 40, Height = h + 40 };
        holder.Children.Add(At(window, 10, 10));
        return ObjectStage(d, holder, holder.Width, holder.Height, Brush(C("#008080")), Colors.White, C("#004040"));
    }

    private static Geometry StarPath(double cx, double cy, double r)
    {
        var points = Enumerable.Range(0, 10).Select(i =>
        {
            var a = (Math.PI / 5 * i) - (Math.PI / 2);
            var rr = i % 2 == 0 ? r : r * .45;
            return new Point(cx + (Math.Cos(a) * rr), cy + (Math.Sin(a) * rr));
        }).ToList();
        return new PathGeometry(new[] { new PathFigure(points[0], points.Skip(1).Select(p => (PathSegment)new LineSegment(p, true)), true) });
    }

    /// <summary>A stroke drawn with Paint's pencil: hard-edged, no smoothing.</summary>
    private static FrameworkElement PixelStroke(Geometry geometry, Color color, double thickness)
    {
        var path = new Path { Data = geometry, Stroke = Brush(color), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);
        return path;
    }
}
