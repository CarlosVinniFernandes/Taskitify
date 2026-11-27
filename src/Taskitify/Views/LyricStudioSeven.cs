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
        var model = d.Text("model");
        var classic = model.StartsWith("Classic", StringComparison.OrdinalIgnoreCase);
        var silver = model.Contains("Silver", StringComparison.OrdinalIgnoreCase);
        var blue = classic ? C("#000080") : silver ? C("#3D4A6E") : C("#1E4FA8");
        var line = classic ? C("#808080") : silver ? C("#B3B3C6") : C("#A9BEDF");
        var labelInk = classic ? C("#000000") : silver ? C("#34344A") : C("#203F78");
        var canvas = new Canvas { Width = w, Height = h };
        Brush Bevel3D() => new LinearGradientBrush(new GradientStopCollection { new(Colors.White, 0), new(Colors.White, .49), new(C("#404040"), .51), new(C("#404040"), 1) }, new Point(0, 0), new Point(1, 1));

        // Window frame: Luna blue, Luna silver, or the grey Windows Classic chrome.
        canvas.Children.Add(new Border
        {
            Width = w, Height = h, CornerRadius = classic ? new CornerRadius(0) : new CornerRadius(12, 12, 4, 4), BorderThickness = new Thickness(classic ? 3 : 4),
            BorderBrush = classic ? Bevel3D() : silver ? new LinearGradientBrush(C("#E6E6EE"), C("#9696AE"), 90) : new LinearGradientBrush(C("#2C66D2"), C("#0B3C9A"), 90),
            Background = classic ? Brush(C("#D4D0C8")) : silver ? new LinearGradientBrush(C("#F8F8FB"), C("#DADAE4"), 90) : new LinearGradientBrush(C("#EEF4FC"), C("#CFDDF2"), 90),
        });
        var titleHeight = classic ? 50.0 : 64.0;
        canvas.Children.Add(At(new Border
        {
            Width = w - (classic ? 12 : 8), Height = titleHeight, CornerRadius = classic ? new CornerRadius(0) : new CornerRadius(9, 9, 0, 0),
            Background = classic ? new LinearGradientBrush(C("#0A246A"), C("#A6CAF0"), 0)
                : silver ? new LinearGradientBrush(new GradientStopCollection { new(C("#FFFFFF"), 0), new(C("#E4E4EC"), .45), new(C("#C2C2D2"), .9), new(C("#A8A8BC"), 1) }, 90)
                : new LinearGradientBrush(new GradientStopCollection { new(C("#3D8CF2"), 0), new(C("#155FD6"), .45), new(C("#0E4DBE"), 1) }, 90),
        }, classic ? 6 : 4, classic ? 6 : 4));
        canvas.Children.Add(At(Butterfly(classic ? 32 : 40), 22, classic ? 14 : 16));
        var titleText = Label(who + " - Conversation", Tahoma, classic ? 22 : 25, FontWeights.Bold, silver ? C("#26263A") : Colors.White, TextAlignment.Left);
        if (!classic && !silver)
            titleText.Effect = new DropShadowEffect { Color = C("#0A2A6A"), BlurRadius = 2, ShadowDepth = 1.5, Opacity = .8 };
        canvas.Children.Add(At(titleText, 70, classic ? 16 : 20));
        foreach (var (glyph, x, red) in new[] { ("–", w - 156.0, false), ("□", w - 108.0, false), ("✕", w - 60.0, true) })
        {
            Border cap;
            if (classic)
                cap = new Border { Width = 38, Height = 34, Background = Brush(C("#D4D0C8")), BorderThickness = new Thickness(2), BorderBrush = Bevel3D(),
                    Child = Label(glyph, Tahoma, 20, FontWeights.Bold, Colors.Black, TextAlignment.Center) };
            else
                cap = new Border
                {
                    Width = 42, Height = 42, CornerRadius = new CornerRadius(6), BorderBrush = Brush(silver ? C("#8C8CA2") : Colors.White), BorderThickness = new Thickness(1.5),
                    Background = silver ? new LinearGradientBrush(C("#FFFFFF"), C("#C8C8D6"), 90)
                        : red ? new LinearGradientBrush(C("#E8806A"), C("#C2381E"), 90) : new LinearGradientBrush(C("#6FA4F5"), C("#2A6BDA"), 90),
                    Child = Label(glyph, Tahoma, 22, FontWeights.Bold, silver ? C("#3A3A50") : Colors.White, TextAlignment.Center),
                };
            ((FrameworkElement)cap.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(cap, classic ? x + 8 : x, classic ? 14 : 15));
        }
        if (classic)
        {
            // The classic window has a menu bar under its title.
            var menu = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var item in new[] { "File", "Edit", "View", "Actions", "Help" })
            {
                var t = new TextBlock { FontFamily = Tahoma, FontSize = 19, Margin = new Thickness(0, 0, 26, 0) };
                t.Inlines.Add(new System.Windows.Documents.Run(item[..1]) { TextDecorations = TextDecorations.Underline });
                t.Inlines.Add(new System.Windows.Documents.Run(item[1..]));
                menu.Children.Add(t);
            }
            canvas.Children.Add(At(menu, 20, 62));
        }
        // Toolbar.
        var tools = new[] { ("", "Invite"), ("", "Send Files"), ("", "Webcam"), ("", "Call"), ("", "Activities"), ("", "Games") };
        for (var i = 0; i < tools.Length; i++)
        {
            var x = 36 + (i * 112);
            var colors = new[] { "#2E7D32", "#E68A00", "#1565C0", "#2E7D32", "#7B3FB2", "#C62828" };
            UIElement icon = Glyph(tools[i].Item1, 28, classic ? C(colors[i]) : blue, HorizontalAlignment.Center);
            if (classic)
                canvas.Children.Add(At(new Border { Width = 58, Height = 52, Child = icon }, x + 16, 96));
            else
                canvas.Children.Add(At(new Border
                {
                    Width = 58, Height = 58, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5),
                    Background = silver ? new LinearGradientBrush(C("#FFFFFF"), C("#D6D6E2"), 90) : new LinearGradientBrush(C("#FFFFFF"), C("#C9DAF3"), 90),
                    BorderBrush = Brush(silver ? C("#A6A6BA") : C("#9DB6DE")), Child = icon,
                }, x + 16, 86));
            var label = Label(tools[i].Item2, Tahoma, 17, FontWeights.Normal, labelInk, TextAlignment.Center);
            label.Width = 100;
            canvas.Children.Add(At(label, x - 4, 150));
        }
        if (classic)
            canvas.Children.Add(At(new Border { Width = w - 24, Height = 2, BorderThickness = new Thickness(0, 1, 0, 1), BorderBrush = Bevel3D() }, 12, 182));
        // To: bar.
        var to = new TextBlock { FontFamily = Tahoma, FontSize = 21, Foreground = Brush(C("#1C2A44")), TextTrimming = TextTrimming.CharacterEllipsis, Width = w - 110 };
        to.Inlines.Add(new System.Windows.Documents.Run("To: ") { FontWeight = FontWeights.Bold });
        to.Inlines.Add(new System.Windows.Documents.Run(who + " "));
        to.Inlines.Add(new System.Windows.Documents.Run("<" + new string(who.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()) + "@hotmail.com>") { Foreground = Brush(C("#7A8496")) });
        to.Inlines.Add(new System.Windows.Documents.Run("  (" + d.Text("status") + ")") { Foreground = Brush(C("#3B8A2E")) });
        Border Pane(double width, double height, UIElement? child = null, Thickness padding = default) => new()
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(classic ? 0 : 6), Background = Brush(Colors.White), Padding = padding, Child = child,
            BorderBrush = classic ? new LinearGradientBrush(C("#808080"), Colors.White, new Point(0, 0), new Point(1, 1)) : Brush(line),
            BorderThickness = new Thickness(classic ? 2 : 1.5),
        };
        canvas.Children.Add(At(Pane(w - 60, 52, to, new Thickness(16, 12, 16, 0)), 30, 194));

        // Conversation.
        const double convX = 30, convY = 262, convW = 650, convH = 650;
        canvas.Children.Add(At(Pane(convW, convH), convX, convY));
        canvas.Children.Add(At(Label(who + " says:", Tahoma, 20, FontWeights.Normal, C("#6B6B6B"), TextAlignment.Left), convX + 22, convY + 22));
        var lines = LyricLines(d.LyricText, face?.Family ?? Trebuchet, face?.Weight ?? FontWeights.Normal, classic ? C("#800000") : C("#1B3FA0"), convW - 64, convH - 230,
            Presence(d, 58), 24, d.Align(TextAlignment.Left), Leading(d, 1.2), gap: 8, wholeShrink: .72);
        canvas.Children.Add(At(lines, convX + 40, convY + 60));
        lines.Measure(new Size(convW - 64, double.PositiveInfinity));
        var replyY = convY + 60 + lines.DesiredSize.Height + 34;
        canvas.Children.Add(At(Label("You say:", Tahoma, 20, FontWeights.Normal, C("#6B6B6B"), TextAlignment.Left), convX + 22, replyY));
        var reply = new StackPanel { Orientation = Orientation.Horizontal };
        reply.Children.Add(Emoticon(34, heart: true));
        reply.Children.Add(Label("  on repeat", Trebuchet, 28, FontWeights.Normal, C("#A3236B"), TextAlignment.Left));
        canvas.Children.Add(At(reply, convX + 40, replyY + 34));
        // A nudge, as the old windows printed it, at the foot of the conversation.
        var nudge = Label("— " + who + " just sent you a nudge! —", Tahoma, 18, FontWeights.Normal, C("#8A8A8A"), TextAlignment.Center);
        nudge.Width = convW - 40;
        canvas.Children.Add(At(nudge, convX + 20, convY + convH - 46));

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
        canvas.Children.Add(At(Pane(650, 150, typed, new Thickness(18, 16, 18, 0)), convX, 978));
        canvas.Children.Add(At(new Border { Width = 2, Height = 30, Background = Brush(C("#1C2A44")) }, convX + 22 + MeasureWidth(d.Text("note"), Trebuchet, 26), 996));
        foreach (var (name, y) in new[] { ("Send", 978.0), ("Search", 1058.0) })
        {
            var button = classic
                ? new Border { Width = 200, Height = 66, Background = Brush(C("#D4D0C8")), BorderThickness = new Thickness(2.5), BorderBrush = Bevel3D(),
                    Child = Label(name, Tahoma, 22, FontWeights.Normal, Colors.Black, TextAlignment.Center) }
                : new Border
                {
                    Width = 200, Height = 66, CornerRadius = new CornerRadius(8), BorderBrush = Brush(silver ? C("#8C8CA4") : C("#7D96C2")), BorderThickness = new Thickness(1.5),
                    Background = silver ? new LinearGradientBrush(C("#FFFFFF"), C("#D9D9E4"), 90) : new LinearGradientBrush(C("#FFFFFF"), C("#D4E1F4"), 90),
                    Child = Label(name, Tahoma, 22, FontWeights.Bold, C("#1C2A44"), TextAlignment.Center),
                };
            ((FrameworkElement)button.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(button, 712, y));
        }
        if (classic)
            canvas.Children.Add(At(new Border { Width = w - 24, Height = 38, BorderThickness = new Thickness(1.5), BorderBrush = new LinearGradientBrush(C("#808080"), Colors.White, new Point(0, 0), new Point(1, 1)) }, 12, 1144));
        canvas.Children.Add(At(Label("Last message received at " + d.Text("time") + " on " + DateTime.Today.ToString("d", CultureInfo.CurrentCulture) + ".",
            Tahoma, 18, FontWeights.Normal, classic ? Colors.Black : C("#55627A"), TextAlignment.Left), 30, 1152));
        var brand = Label("♪ Taskitify Messenger", Tahoma, 18, FontWeights.Bold, classic ? C("#000080") : silver ? C("#4A5578") : C("#2F6FD0"), TextAlignment.Right);
        brand.Width = 300;
        canvas.Children.Add(At(brand, w - 330, 1152));

        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 50, ShadowDepth = 18, Direction = 290, Opacity = .45 };
        return classic
            ? ObjectStage(d, canvas, w, h, Brush(C("#3A6EA5")), Colors.White, C("#1E3F66"))
            : ObjectStage(d, canvas, w, h, Bliss(), Colors.White, C("#2A5C2A"));
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
        // Measured from the handheld: each half 133 x 74 mm, 3-inch 4:3 screens (61 x 46 mm)
        // centred, the hinge a cylinder across the lower half's top edge.
        const double w = 900, half = 500, hinge = 56, h = (half * 2) + hinge;
        var model = d.Text("model");
        var (preset, keys, print) = model switch
        {
            "Black" => (C("#1F2026"), C("#2C2D33"), C("#8D909A")),
            "Pink" => (C("#F2A3B5"), C("#F8C6D2"), C("#B0586E")),
            "Blue" => (C("#A9C8E6"), C("#D2E3F3"), C("#5E7FA3")),
            _ => (C("#F3F4F6"), C("#FBFBFC"), C("#9A9EA6")),
        };
        var shell = d.Color("device", preset);
        var dark = Luminance(shell) < .3;
        var ink = C("#22262E");
        var face = d.UserFace;
        var canvas = new Canvas { Width = w, Height = h };
        Brush Gloss(Color c) => new LinearGradientBrush(new GradientStopCollection { new(Lighten(c, dark ? .18 : .5), 0), new(c, .28), new(Darken(c, dark ? .8 : .9), 1) }, 90);
        Brush Key(Color c) => new RadialGradientBrush(Lighten(c, .3), Darken(c, dark ? .7 : .82)) { GradientOrigin = new Point(.38, .3) };
        const double sw = 416, sh = 312;

        // Top half: the lid with its glass lens, the screen and the two speaker grilles.
        canvas.Children.Add(At(new Border { Width = w, Height = half, CornerRadius = new CornerRadius(46, 46, 16, 16), Background = Gloss(shell),
            BorderBrush = Brush(Darken(shell, dark ? .6 : .8)), BorderThickness = new Thickness(2) }, 0, 0));
        canvas.Children.Add(At(new Border { Width = w - 16, Height = half - 16, CornerRadius = new CornerRadius(40, 40, 12, 12), BorderThickness = new Thickness(2),
            BorderBrush = new LinearGradientBrush(Alpha(Colors.White, dark ? (byte)40 : (byte)160), Alpha(Colors.White, 0), 90) }, 8, 8));
        // The lens: one piece of glass, black around the screen, with a diagonal sheen.
        canvas.Children.Add(At(new Border { Width = sw + 92, Height = sh + 80, CornerRadius = new CornerRadius(12), Background = new LinearGradientBrush(C("#26282E"), C("#0E0F12"), 90),
            BorderBrush = Brush(Darken(shell, .7)), BorderThickness = new Thickness(1.5) }, (w - sw - 92) / 2, 58));
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
        top.Children.Add(new Border { Background = DotMatrix(Colors.Black), Opacity = .5 });
        canvas.Children.Add(At(new Viewbox { Width = sw, Height = sh, Child = top, Stretch = Stretch.Fill }, (w - sw) / 2, 98));
        canvas.Children.Add(At(new Border { Width = sw + 92, Height = sh + 80, CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(Colors.White, 26), 0), new(Alpha(Colors.White, 0), .42), new(Alpha(Colors.White, 0), 1) }, new Point(0, 0), new Point(1, 1)) }, (w - sw - 92) / 2, 58));
        foreach (var sx in new[] { 92.0, 772.0 })
            for (var r = 0; r < 5; r++)
                for (var cc = 0; cc < 2; cc++)
                    canvas.Children.Add(At(new Ellipse { Width = 9, Height = 9, Fill = Brush(Darken(shell, dark ? .4 : .5)),
                        Stroke = Brush(Alpha(Colors.White, dark ? (byte)30 : (byte)140)), StrokeThickness = .8 }, sx + (cc * 20) + (r % 2 * 10), 200 + (r * 18)));

        // Hinge: the cylinder, the shoulder buttons' edges at its ends, the microphone and the lights.
        canvas.Children.Add(At(new Border { Width = 82, Height = hinge - 8, CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(C("#C8CACF"), C("#8E9198"), 90) }, 6, half + 4));
        canvas.Children.Add(At(new Border { Width = 82, Height = hinge - 8, CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(C("#C8CACF"), C("#8E9198"), 90) }, w - 88, half + 4));
        canvas.Children.Add(At(new Border { Width = w - 200, Height = hinge, CornerRadius = new CornerRadius(hinge / 2),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Darken(shell, .72), 0), new(Lighten(shell, dark ? .2 : .45), .4), new(shell, .6), new(Darken(shell, .68), 1) }, 90) }, 100, half));
        canvas.Children.Add(At(new Ellipse { Width = 7, Height = 7, Fill = Brush(Darken(shell, .4)) }, (w / 2) - 3.5, half + 24));
        canvas.Children.Add(At(new Ellipse { Width = 11, Height = 11, Fill = Brush(C("#43D35A")), Effect = new DropShadowEffect { Color = C("#43D35A"), BlurRadius = 10, ShadowDepth = 0 } }, 712, half + 22));
        canvas.Children.Add(At(new Ellipse { Width = 11, Height = 11, Fill = Brush(Darken(shell, .6)) }, 736, half + 22));
        canvas.Children.Add(At(Label("⏻  ⚡", Symbols, 12, FontWeights.Normal, print, TextAlignment.Left), 708, half + 36));

        // Lower half: the touch screen in its shallow well, the cross pad, X/A/B/Y, start and select.
        var y0 = half + hinge;
        canvas.Children.Add(At(new Border { Width = w, Height = half, CornerRadius = new CornerRadius(16, 16, 46, 46), Background = Gloss(shell),
            BorderBrush = Brush(Darken(shell, dark ? .6 : .8)), BorderThickness = new Thickness(2) }, 0, y0));
        canvas.Children.Add(At(new Border { Width = sw + 30, Height = sh + 30, CornerRadius = new CornerRadius(6), Background = Brush(Darken(shell, dark ? .75 : .86)),
            BorderBrush = new LinearGradientBrush(Darken(shell, .6), Lighten(shell, .4), 90), BorderThickness = new Thickness(2) }, (w - sw - 30) / 2, y0 + 64));
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
        var said = LyricLines(d.LyricText, face?.Family ?? Vt323, face?.Weight ?? FontWeights.Normal, ink, 404, 226, Presence(d, 54), 26,
            d.Align(TextAlignment.Left), Leading(d, 1.0), gap: 4, wholeShrink: .6);
        said.VerticalAlignment = VerticalAlignment.Center;
        dialog.Children.Add(said);
        box.Child = dialog;
        touch.Children.Add(box);
        canvas.Children.Add(At(new Viewbox { Width = sw, Height = sh, Child = touch, Stretch = Stretch.Fill }, (w - sw) / 2, y0 + 79));
        // Stylus marks: faint scratches where the stylus has been.
        canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,0 C40,-6 90,4 130,-2 M20,30 C60,26 80,34 110,30"), Stroke = Brush(Alpha(Colors.White, 50)), StrokeThickness = 1.2 }, (w - sw) / 2 + 220, y0 + 330));

        // Cross pad.
        var pad = new Grid { Width = 132, Height = 132 };
        foreach (var (pw, ph) in new[] { (44.0, 132.0), (132.0, 44.0) })
            pad.Children.Add(new Border { Width = pw, Height = ph, CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Lighten(keys, .2), Darken(keys, dark ? .7 : .8), 90),
                BorderBrush = Brush(Darken(keys, .7)), BorderThickness = new Thickness(1.2), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2.5, Opacity = .3 } });
        pad.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = Brush(Darken(keys, .85)) });
        canvas.Children.Add(At(pad, 60, y0 + 136));
        // X A B Y.
        const double cx = 786, cy = 200;
        foreach (var (letter, dx, dy) in new[] { ("X", 0.0, -48.0), ("A", 48.0, 0.0), ("B", 0.0, 48.0), ("Y", -48.0, 0.0) })
        {
            var key = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Key(keys),
                BorderBrush = Brush(Darken(keys, .7)), BorderThickness = new Thickness(1.2), Child = Label(letter, Gill, 21, FontWeights.SemiBold, print, TextAlignment.Center),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2.5, Opacity = .3 } };
            ((FrameworkElement)key.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(key, cx + dx - 22, y0 + cy + dy - 22));
        }
        foreach (var (name, yy) in new[] { ("START", 330.0), ("SELECT", 372.0) })
        {
            canvas.Children.Add(At(new Ellipse { Width = 24, Height = 24, Fill = Key(keys), Stroke = Brush(Darken(keys, .7)), StrokeThickness = 1.2 }, 752, y0 + yy));
            canvas.Children.Add(At(Label(name, Gill, 14, FontWeights.SemiBold, print, TextAlignment.Left), 784, y0 + yy + 4));
        }
        var engraving = Label(d.Text("note").ToUpperInvariant(), Gill, 17, FontWeights.SemiBold, Alpha(print, 200), TextAlignment.Center);
        engraving.Width = 400;
        canvas.Children.Add(At(engraving, 250, y0 + half - 64));
        canvas.Children.Add(At(Label("●", Symbols, 9, FontWeights.Normal, print, TextAlignment.Left), 140, y0 + 300));
        canvas.Children.Add(At(Label("●", Symbols, 9, FontWeights.Normal, print, TextAlignment.Left), 154, y0 + 300));
        canvas.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 22, Direction = 285, Opacity = .45 };
        var desk = dark ? Desk(C("#9AA4B8"), C("#3A4258")) : model == "Pink" ? Desk(C("#E9C9D2"), C("#6E4656")) : Desk(C("#8FA3C8"), C("#2C3552"));
        return ObjectStage(d, canvas, w, h, desk, Colors.White, C("#2C3552"));
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
        var model = d.Text("model");
        var sticker = d.Text("sticker");
        var sideB = d.Text("side") == "B";
        var canvas = new Canvas { Width = w, Height = h };
        var shortText = d.LyricText.Length <= 50;
        var rule = shortText ? 84.0 : 64.0;

        // The J-card, under the tape.
        var card = new Grid { Width = 860, Height = 880, Background = Fibers(sticker == "Kraft" ? C("#D9BE96") : C("#F6F0E2"), Alpha(C("#A08660"), 34)) };
        card.Children.Add(new Border { Height = 92, VerticalAlignment = VerticalAlignment.Top, Background = sticker == "Rainbow" ? RetroStripes(0) : new LinearGradientBrush(Lighten(label, .1), Darken(label, .85), 0) });
        card.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(110, 92, 0, 0), Background = Brush(Alpha(C("#5A4A2E"), 60)) });
        var rules = new DrawingGroup();
        rules.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 10, rule))));
        rules.Children.Add(new GeometryDrawing(Brush(Alpha(C("#7E9DC4"), 90)), null, new RectangleGeometry(new Rect(0, rule - 1.4, 10, 1.4))));
        card.Children.Add(new Border { Margin = new Thickness(130, 188, 40, 60), Background = new DrawingBrush(rules) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, rule), ViewportUnits = BrushMappingMode.Absolute } });
        var cardText = new Canvas();
        cardText.Children.Add(At(Spaced("SIDE " + d.Text("side"), Figtree, 30, FontWeights.Black, Colors.White, TextAlignment.Left, .3), 36, 26));
        cardText.Children.Add(At(Handwriting(d.Text("title"), HandFont, 60, pen, 640, 51, TextAlignment.Left, 64), 140, 92));
        cardText.Children.Add(At(Handwriting("♪", HandFont, 46, pen, 60, 52, TextAlignment.Left, 64), 48, 186));
        // The lines get the card's best space: the rules widen for a short lyric, the hand stays large.
        var area = 700 - 196;
        var body = Handwriting(d.LyricText, face?.Family ?? HandFont, Math.Min(Presence(d, shortText ? 80 : 68), rule * 1.12), pen, 680, 53, d.Align(TextAlignment.Left), rule,
            maxHeight: Math.Floor(area / rule) * rule, weight: face?.Weight ?? FontWeights.SemiBold, preferWhole: d.LyricText.Length <= 110);
        cardText.Children.Add(At(body, 140, 196));
        // The track list, as written on every mixtape card.
        var track = new Grid { Width = 680 };
        track.Children.Add(Handwriting(d.Text("side") + "1  " + d.Title + " — " + d.Artist, HandFont, 34, Alpha(pen, 220), 560, 55, TextAlignment.Left, 44, maxHeight: 46));
        var len = Handwriting(d.Duration, HandFont, 34, Alpha(pen, 220), 110, 56, TextAlignment.Right, 44);
        len.HorizontalAlignment = HorizontalAlignment.Right;
        track.Children.Add(len);
        cardText.Children.Add(At(track, 140, 716));
        cardText.Children.Add(At(Handwriting(d.Text("to") + " →", HandFont, 46, Alpha(C("#9C2E3A"), 235), 520, 54, TextAlignment.Right, 60), 300, 790));
        card.Children.Add(cardText);
        card.RenderTransformOrigin = new Point(.5, .5);
        card.RenderTransform = new RotateTransform(2.2);
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 14, Direction = 280, Opacity = .35 };
        canvas.Children.Add(At(card, 70, 640));

        // The cassette: black, smoked clear, white or red plastic.
        var (top, bottom, metal, lower) = model switch
        {
            "Clear" => (Color.FromArgb(120, 190, 198, 206), Color.FromArgb(150, 120, 128, 138), C("#C9CCD2"), Color.FromArgb(150, 90, 96, 106)),
            "White" => (C("#F4F2EC"), C("#CFCAC0"), C("#9A9AA2"), C("#DCD7CC")),
            "Red" => (C("#D23A36"), C("#7E1A1A"), C("#B9B9C0"), C("#8E2020")),
            _ => (C("#3A3B42"), C("#121318"), C("#9A9AA2"), C("#1F2026")),
        };
        var light = Luminance(top) > .6;
        var tape = new Canvas { Width = 1000, Height = 630 };
        var shellPath = RoundRect(0, 0, 1000, 630, 34, 34, 34, 34);
        if (model == "Clear")
        {
            // Through smoked plastic: the full tape packs, the guide rollers and the felt pad.
            tape.Children.Add(At(new Ellipse { Width = 330, Height = 330, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")), Opacity = .9 }, sideB ? 590 - 125 : 112, 120));
            tape.Children.Add(At(new Ellipse { Width = 230, Height = 230, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")), Opacity = .9 }, sideB ? 162 : 590, 170));
            foreach (var x in new[] { 120.0, 860.0 })
                tape.Children.Add(At(new Ellipse { Width = 30, Height = 30, Fill = Brush(Alpha(Colors.White, 200)) }, x, 560));
            tape.Children.Add(At(new Rectangle { Width = 60, Height = 14, Fill = Brush(C("#C9B27A")) }, 470, 596));
        }
        tape.Children.Add(new Path { Data = shellPath, Fill = new LinearGradientBrush(new GradientStopCollection { new(top, 0), new(Mid(top, bottom), .6), new(bottom, 1) }, 90),
            Stroke = Brush(light ? C("#A9A49A") : C("#0B0B0E")), StrokeThickness = 3 });
        tape.Children.Add(new Path { Data = shellPath, Fill = new LinearGradientBrush(Alpha(Colors.White, 40), Alpha(Colors.White, 0), new Point(0, 0), new Point(.4, .6)) });
        // Moulded ridges along the sides.
        foreach (var x in new[] { 18.0, 970.0 })
            for (var k = 0; k < 6; k++)
                tape.Children.Add(At(new Rectangle { Width = 12, Height = 3, Fill = Brush(Alpha(light ? Colors.Black : Colors.White, 40)) }, x, 240 + (k * 22)));
        foreach (var (x, y) in new[] { (26.0, 26.0), (954.0, 26.0), (26.0, 584.0), (954.0, 584.0), (490.0, 584.0) })
            tape.Children.Add(At(Screw(metal, 24), x - 2, y - 2));
        // Label sticker.
        var stick = new Grid { Width = 880, Height = 380 };
        switch (sticker)
        {
            case "Kraft":
                stick.Background = Fibers(C("#CDAE84"), Alpha(C("#7A5A34"), 46));
                stick.Children.Add(new Border { Margin = new Thickness(14), BorderBrush = Brush(Alpha(C("#3A2A1A"), 120)), BorderThickness = new Thickness(2) });
                break;
            case "Rainbow":
                stick.Background = Fibers(C("#F6EEDC"), Alpha(C("#C2B391"), 26));
                stick.Children.Add(new Border { Height = 120, VerticalAlignment = VerticalAlignment.Bottom, Background = RetroStripes(90) });
                break;
            case "Plain":
                stick.Background = Fibers(C("#FBFAF6"), Alpha(C("#C2B391"), 18));
                stick.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 96, 0, 0), Background = Brush(Alpha(C("#D9534F"), 170)) });
                break;
            default:
                stick.Background = Fibers(C("#F8F2E2"), Alpha(C("#C2B391"), 26));
                stick.Children.Add(new Border { Height = 70, VerticalAlignment = VerticalAlignment.Top, Background = new LinearGradientBrush(Lighten(label, .12), label, 0) });
                stick.Children.Add(new Border { Height = 56, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(label, Darken(label, .8), 0) });
                break;
        }
        foreach (var y in new[] { 112.0, 164.0 })
            stick.Children.Add(new Border { Height = 1.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(130, y, 40, 0), Background = Brush(Alpha(C(sticker == "Plain" ? "#7E9DC4" : "#5A4A2E"), 80)) });
        var sideBox = sticker == "Kraft"
            ? new Border { Width = 86, Height = 86, BorderBrush = Brush(Alpha(C("#B3262E"), 210)), BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(43),
                Child = Label(d.Text("side"), Figtree, 52, FontWeights.Black, Alpha(C("#B3262E"), 220), TextAlignment.Center), RenderTransform = new RotateTransform(-8), OpacityMask = Speckle(91, .6) }
            : new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(8), Background = Brush(C("#1E2026")), Child = Label(d.Text("side"), Figtree, 54, FontWeights.Black, sticker == "Rainbow" ? C("#F29E38") : label, TextAlignment.Center) };
        ((FrameworkElement)sideBox.Child).VerticalAlignment = VerticalAlignment.Center;
        sideBox.HorizontalAlignment = HorizontalAlignment.Left;
        sideBox.VerticalAlignment = VerticalAlignment.Top;
        sideBox.Margin = new Thickness(30, 84, 0, 0);
        stick.Children.Add(sideBox);
        var marker = sticker == "Kraft" ? C("#1A1A1A") : pen;
        var stickerText = new Canvas();
        stickerText.Children.Add(At(Handwriting(d.Text("title"), HandFont, 50, marker, 600, 61, TextAlignment.Left, 52), 136, 64));
        stickerText.Children.Add(At(Handwriting(d.Artist + " • " + d.Title, HandFont, 34, Alpha(marker, 210), 600, 62, TextAlignment.Left, 52), 140, 118));
        stickerText.Children.Add(At(Label(sticker == "Rainbow" ? "C-90" : "C-60", Figtree, 40, FontWeights.Black, C("#1E2026"), TextAlignment.Right), 740, 14));
        stickerText.Children.Add(At(Label(sideB ? "◀◀  SIDE B" : "SIDE A  ▶▶", Figtree, 15, FontWeights.Bold, Alpha(C("#1E2026"), 170), TextAlignment.Left), 40, 22));
        stick.Children.Add(stickerText);
        tape.Children.Add(At(stick, 60, 40));
        // Window with reels and tape: the pack is on the side that has been played.
        tape.Children.Add(At(new Border { Width = 470, Height = 140, CornerRadius = new CornerRadius(70), Background = Brush(model == "Clear" ? Color.FromArgb(140, 13, 14, 18) : C("#0D0E12")),
            BorderBrush = Brush(light ? C("#BDB8AE") : C("#2C2D34")), BorderThickness = new Thickness(4) }, 265, 230));
        var (bigX, smallX) = sideB ? (590.0, 222.0) : (222.0, 625.0);
        tape.Children.Add(At(new Ellipse { Width = 210, Height = 210, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")) }, bigX, 196));
        tape.Children.Add(At(new Ellipse { Width = 150, Height = 150, Fill = new RadialGradientBrush(C("#6B4A2E"), C("#3A2614")) }, smallX + (sideB ? 30 : 0), 226));
        foreach (var hub in new[] { 327.0, 700.0 })
        {
            tape.Children.Add(At(new Ellipse { Width = 86, Height = 86, Fill = Brush(C("#E8E6E0")), Stroke = Brush(C("#9A9AA2")), StrokeThickness = 3 }, hub - 43, 257));
            for (var k = 0; k < 6; k++)
            {
                var angle = k * Math.PI / 3;
                tape.Children.Add(At(new Rectangle { Width = 10, Height = 18, Fill = Brush(C("#9A9AA2")), RenderTransformOrigin = new Point(.5, .5),
                    RenderTransform = new RotateTransform(k * 60) }, hub - 5 + (Math.Sin(angle) * 22), 291 - (Math.Cos(angle) * 22)));
            }
        }
        // Window scale marks.
        for (var k = 0; k < 5; k++)
            tape.Children.Add(At(new Rectangle { Width = 2, Height = k % 2 == 0 ? 16 : 10, Fill = Brush(Alpha(Colors.White, 120)) }, 440 + (k * 30), 236));
        tape.Children.Add(new Path { Data = Geometry.Parse("M190,630 L250,450 L750,450 L810,630 Z"), Fill = new LinearGradientBrush(Lighten(lower, .1), lower, 90), Stroke = Brush(light ? C("#A9A49A") : C("#0B0B0E")), StrokeThickness = 2 });
        tape.Children.Add(At(new Rectangle { Width = 600, Height = 10, Fill = Brush(C("#4A3220")) }, 200, 600));
        foreach (var x in new[] { 300.0, 400.0, 576.0, 676.0 })
            tape.Children.Add(At(new Ellipse { Width = 28, Height = 28, Fill = Brush(C("#08080A")) }, x, 520));
        var tapeNote = Label(d.Text("note"), Gill, 17, FontWeights.SemiBold, light ? C("#6A6A72") : C("#B9B9C0"), TextAlignment.Center);
        tapeNote.Width = 400;
        tape.Children.Add(At(tapeNote, 300, 470));
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
        var aged = d.Text("paper").StartsWith("Aged", StringComparison.OrdinalIgnoreCase);
        var ink = aged ? C("#4E463C") : C("#24242A");
        var paperColor = aged ? C("#EFE4C6") : C("#F8F7F2");
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
            Child = Label(d.Text("title").ToUpperInvariant(), Courier, 26, FontWeights.Bold, paperColor, TextAlignment.Center) };
        stack.Children.Add(header);
        stack.Children.Add(Space(Pair2("RECEIPT #" + d.Text("id"), d.Text("date").ToUpperInvariant(), 21, false, 4), 14));
        stack.Children.Add(Pair2("CASHIER: TASKITIFY", d.Time, 21, false, 6));
        stack.Children.Add(Dots());
        stack.Children.Add(Pair2("QTY  ITEM", "PRICE", 21, true, 8));
        stack.Children.Add(Space(Thermal("1 x", 22, ink, 10, true), 10));
        var lyric = face is null
            ? LyricLines(d.LyricText, Courier, FontWeights.Bold, Alpha(ink, 228), w - 90, 620, Presence(d, 48), 22, d.Align(TextAlignment.Left), Leading(d, 1.12), gap: 6, wholeShrink: .66)
            : LyricLines(d.LyricText, face.Value.Family, face.Value.Weight, ink, w - 90, 620, Presence(d, 48), 22, d.Align(TextAlignment.Left), Leading(d, 1.14), gap: 6, wholeShrink: .66);
        if (face is null)
            lyric.OpacityMask = Speckle(5, aged ? .55 : .8);
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

        var edge = new PointCollection();
        for (var x = 0.0; x <= w; x += 20)
            edge.Add(new Point(x, (x / 20) % 2 == 0 ? 0 : 12));
        for (var x = w; x >= 0; x -= 20)
            edge.Add(new Point(x, h - ((x / 20) % 2 == 0 ? 0 : 12)));
        var paper = new Grid { Width = w, Height = h, Clip = new PathGeometry(new[] { new PathFigure(edge[0], edge.Skip(1).Select(p => (PathSegment)new LineSegment(p, false)), true) }) };
        paper.Children.Add(new Border { Background = Fibers(paperColor, Alpha(C(aged ? "#A8946A" : "#C9C6BC"), aged ? (byte)52 : (byte)26)) });
        if (aged)
        {
            // Years in a wallet: the edges have browned, the print has faded unevenly to the right.
            paper.Children.Add(new Border { Background = new RadialGradientBrush(Alpha(C("#C9A86A"), 0), Alpha(C("#B08A4A"), 90)) { RadiusX = .8, RadiusY = .62 } });
            paper.Children.Add(new Ellipse { Width = 260, Height = 240, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * .6, -70, 0),
                StrokeThickness = 10, Stroke = new LinearGradientBrush(Alpha(C("#7A4E22"), 60), Alpha(C("#7A4E22"), 10), 40) });
        }
        paper.Children.Add(stack);
        paper.Children.Add(new Border { Background = aged
            ? new LinearGradientBrush(new GradientStopCollection { new(Alpha(paperColor, 0), .2), new(Alpha(paperColor, 120), .7), new(Alpha(paperColor, 170), 1) }, new Point(0, 0), new Point(1, 1))
            : new LinearGradientBrush(new GradientStopCollection { new(Alpha(paperColor, 0), .55), new(Alpha(paperColor, 70), 1) }, 90) });
        foreach (var fold in aged ? new[] { .28, .42, .71 } : new[] { .42 })
            paper.Children.Add(new Border { Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * fold, 0, 0),
                Background = new LinearGradientBrush(new GradientStopCollection { new(Alpha(Colors.Black, 0), 0), new(Alpha(Colors.Black, aged ? (byte)34 : (byte)20), .45), new(Alpha(Colors.White, aged ? (byte)90 : (byte)140), .55), new(Alpha(Colors.White, 0), 1) }, 90) });
        paper.Children.Add(new Border { Background = new LinearGradientBrush(Alpha(Colors.Black, 0), Alpha(Colors.Black, 26), 0), Width = 60, HorizontalAlignment = HorizontalAlignment.Right });
        paper.RenderTransformOrigin = new Point(.5, .5);
        paper.RenderTransform = new RotateTransform(aged ? 1.8 : -1.6);
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
        var rarity = d.Text("rarity").ToLowerInvariant();
        var holo = rarity.Contains("holo");
        var legendary = rarity.Contains("legend") || rarity.Contains("myth");
        var common = rarity.Contains("common") && !rarity.Contains("uncommon");
        var baseFrame = Luminance(d.Accent) > .6 ? Darken(d.Accent, .7) : d.Accent;
        var frame = common ? Mid(baseFrame, C("#7E7A72")) : legendary ? Mid(baseFrame, C("#B8913A")) : baseFrame;
        var parchment = C("#EDE3C8");
        var ink = C("#1F1A14");
        var face = d.UserFace;
        // The metal of the trims: none on a common, silver on a rare, gold on a legend, rainbow on a foil.
        Brush Metal() => common
            ? new LinearGradientBrush(Darken(frame, .55), Darken(frame, .4), 90)
            : legendary || holo
                ? new LinearGradientBrush(new GradientStopCollection { new(C("#FFF1B8"), 0), new(C("#D9A93A"), .35), new(C("#8A6414"), .55), new(C("#F2D27A"), .8), new(C("#9C7420"), 1) }, new Point(0, 0), new Point(1, 1))
                : new LinearGradientBrush(new GradientStopCollection { new(C("#FFFFFF"), 0), new(C("#B9BEC8"), .35), new(C("#6E7480"), .55), new(C("#E1E4EA"), .8), new(C("#8A909C"), 1) }, new Point(0, 0), new Point(1, 1));
        var gem = legendary ? C("#E2632B") : common ? C("#2A2A2A") : C("#C9A43A");
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new Path { Data = RoundRect(0, 0, w, h, 38, 38, 38, 38), Fill = Brush(C("#0C0C0E")) });
        // Frame: textured stone-and-ink, a metal line inside it.
        canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 110, CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Lighten(frame, .35), 0), new(frame, .4), new(Darken(frame, .7), 1) }, 45),
            BorderBrush = Brush(Darken(frame, .55)), BorderThickness = new Thickness(3) }, 28, 28));
        canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 110, CornerRadius = new CornerRadius(16), Background = Fibers(Alpha(Colors.White, 0), Alpha(Colors.Black, 40), 1.6) }, 28, 28));
        canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 110, CornerRadius = new CornerRadius(16), Background = Marble(frame) }, 28, 28));
        canvas.Children.Add(At(new Border { Width = w - 84, Height = h - 138, CornerRadius = new CornerRadius(12), BorderBrush = Metal(), BorderThickness = new Thickness(common ? 1.5 : 3) }, 42, 42));
        if (legendary || holo)
            foreach (var (x, y, a) in new[] { (44.0, 44.0, 0.0), (w - 44, 44.0, 90.0), (w - 44, h - 96, 180.0), (44.0, h - 96, 270.0) })
                canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,0 L60,0 C46,4 34,10 30,22 C40,22 46,30 40,38 C34,30 26,32 22,40 C10,34 4,46 0,60 Z M14,14 m-5,0 a5,5 0 1,0 10,0 a5,5 0 1,0 -10,0"),
                    Fill = Metal(), Stroke = Brush(Alpha(Colors.Black, 90)), StrokeThickness = 1, RenderTransform = new RotateTransform(a) }, x, y));

        // Bars with notched ends and metal edges.
        FrameworkElement Bar(UIElement content, double height)
        {
            var g = new Grid { Width = w - 110, Height = height };
            var shape = Geometry.Parse(FormattableString.Invariant($"M16,0 H{w - 126} L{w - 110},{height / 2} L{w - 126},{height} H16 L0,{height / 2} Z"));
            g.Children.Add(new Path { Data = shape, Fill = new LinearGradientBrush(Lighten(frame, .84), Lighten(frame, .55), 90), Stroke = Metal(), StrokeThickness = common ? 2.5 : 4.5,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 3, Opacity = .4 } });
            g.Children.Add(new Path { Data = shape, Fill = new LinearGradientBrush(Alpha(Colors.White, 110), Alpha(Colors.White, 0), 90), Margin = new Thickness(6, 4, 6, height * .5), Stretch = Stretch.Fill });
            if (content is FrameworkElement fe)
                fe.Margin = new Thickness(34, 0, 26, 0);
            g.Children.Add(content);
            return g;
        }
        // Legendary crown: a gilded arch rising over the name.
        if (legendary)
        {
            canvas.Children.Add(new Path { Data = Geometry.Parse("M120,64 C210,14 330,40 450,10 C570,40 690,14 780,64 L760,64 C680,34 570,58 450,30 C330,58 220,34 140,64 Z"),
                Fill = Metal(), Stroke = Brush(Alpha(Colors.Black, 110)), StrokeThickness = 1.2 });
            canvas.Children.Add(At(new Path { Data = Geometry.Parse("M14,0 L28,16 L14,32 L0,16 Z"), Fill = new LinearGradientBrush(C("#FF9A6A"), C("#A3260E"), 90), Stroke = Metal(), StrokeThickness = 2.5 }, 436, 4));
        }
        var nameRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var cost = new StackPanel { Orientation = Orientation.Horizontal };
        var pipColors = new[] { "#F7F1C6", "#A9D4F0", "#CFC2BC", "#F4A98C", "#A2D6B0" };
        var i0 = 0;
        foreach (var symbol in d.Text("symbol").Where(c => !char.IsWhiteSpace(c)).Take(4))
        {
            var pc = C(pipColors[i0++ % pipColors.Length]);
            var pip = new Grid { Width = 48, Height = 48, Margin = new Thickness(6, 0, 0, 0) };
            pip.Children.Add(new Ellipse { Fill = Brush(C("#141414")), Margin = new Thickness(-3, 3, 3, -3) });
            pip.Children.Add(new Ellipse { Fill = new RadialGradientBrush(Lighten(pc, .4), Darken(pc, .82)) { GradientOrigin = new Point(.35, .3) } });
            var glyph = Label(symbol.ToString(), Symbols, 24, FontWeights.Normal, C("#141414"), TextAlignment.Center);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            pip.Children.Add(glyph);
            cost.Children.Add(pip);
        }
        DockPanel.SetDock(cost, Dock.Right);
        nameRow.Children.Add(cost);
        var name = Label(d.Title, DmSerif, 44, FontWeights.Normal, ink, TextAlignment.Left);
        name.VerticalAlignment = VerticalAlignment.Center;
        nameRow.Children.Add(name);
        canvas.Children.Add(At(Bar(nameRow, 84), 55, 54));
        // Art in a bevelled metal frame.
        var art = new Border { Width = w - 130, Height = 500, BorderThickness = new Thickness(7), BorderBrush = Metal(),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = .5 } };
        var artInner = new Grid();
        artInner.Children.Add(d.Photo(486, 0, w - 144) ?? Artwork(486, d.Accent, 1));
        artInner.Children.Add(new Border { BorderBrush = Brush(Alpha(Colors.Black, 160)), BorderThickness = new Thickness(2) });
        art.Child = artInner;
        canvas.Children.Add(At(art, 65, 150));
        // Type line with the set symbol in the rarity's colour.
        var typeRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var set = new Path { Data = Geometry.Parse("M20,0 L40,14 L32,40 L8,40 L0,14 Z M20,9 L30,16 L26,32 L14,32 L10,16 Z"), Fill = new LinearGradientBrush(Lighten(gem, .5), Darken(gem, .65), 90),
            Stroke = Brush(C("#141414")), StrokeThickness = 2, Width = 42, Height = 42, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(set, Dock.Right);
        typeRow.Children.Add(set);
        var type = Label((legendary ? "Legendary " : "") + d.Text("type") + " — " + d.Text("category"), DmSerif, 34, FontWeights.Normal, ink, TextAlignment.Left);
        type.VerticalAlignment = VerticalAlignment.Center;
        typeRow.Children.Add(type);
        canvas.Children.Add(At(Bar(typeRow, 72), 55, 664));
        // Text box: parchment with a watermark, the lines as flavour text, an ornament, the credit.
        var text = new Grid { Width = w - 130, Height = 396, Background = Fibers(parchment, Alpha(C("#A98F5E"), 40)) };
        text.Children.Add(new Border { BorderBrush = Metal(), BorderThickness = new Thickness(common ? 3 : 5) });
        var flavour = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(46, 22, 46, 22) };
        flavour.Children.Add(LyricLines(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, ink, w - 222, 276, Presence(d, 50), 22,
            d.Align(TextAlignment.Left), Leading(d, 1.22), gap: 4, wholeShrink: .7, italic: face is null));
        var orn = Label("❦", Symbols, 22, FontWeights.Normal, Alpha(ink, 150), TextAlignment.Center);
        orn.Margin = new Thickness(0, 10, 0, 0);
        flavour.Children.Add(orn);
        var credit = Label("— " + d.Artist, Georgia, 26, FontWeights.Normal, Alpha(ink, 200), TextAlignment.Right);
        credit.FontStyle = FontStyles.Italic;
        flavour.Children.Add(credit);
        text.Children.Add(flavour);
        canvas.Children.Add(At(text, 65, 750));
        // The song's length where a creature's strength would be.
        var pt = new Border { Width = 160, Height = 70, CornerRadius = new CornerRadius(12), Background = new LinearGradientBrush(Lighten(frame, .84), Lighten(frame, .5), 90),
            BorderBrush = Metal(), BorderThickness = new Thickness(4), Child = Label(d.Duration, DmSerif, 36, FontWeights.Normal, ink, TextAlignment.Center),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 3, Opacity = .45 } };
        ((FrameworkElement)pt.Child).VerticalAlignment = VerticalAlignment.Center;
        canvas.Children.Add(At(pt, w - 236, 1110));
        // Security stamp, on anything above common.
        if (!common)
        {
            var stamp = new Path { Data = new EllipseGeometry(new Point(40, 22), 40, 22), Stroke = Brush(Alpha(Colors.Black, 120)), StrokeThickness = 1.5,
                Fill = holo ? Rainbow(.9) : Metal() };
            canvas.Children.Add(At(stamp, (w / 2) - 40, 1140));
        }
        // Collector line.
        canvas.Children.Add(At(Label(d.Text("id") + "  " + char.ToUpperInvariant(rarity.FirstOrDefault('c')) + "\n♪ TSK • EN", Figtree, 18, FontWeights.Bold, C("#EDE6D8"), TextAlignment.Left), 50, 1168));
        var illus = Label("✎ Illus. " + d.Artist, Figtree, 19, FontWeights.SemiBold, C("#EDE6D8"), TextAlignment.Left);
        canvas.Children.Add(At(illus, 210, 1178));
        var rights = Label("™ & © Taskitify", Figtree, 16, FontWeights.SemiBold, C("#B8B0A0"), TextAlignment.Right);
        rights.Width = 200;
        canvas.Children.Add(At(rights, w - 260, 1196));
        // Foil: a faint sheen on rares, the full rainbow with sparkles on a holographic.
        if (holo)
        {
            canvas.Children.Add(new Path { Data = RoundRect(0, 0, w, h, 38, 38, 38, 38), Fill = Rainbow(.34), IsHitTestVisible = false });
            var rnd = new Random(27);
            for (var k = 0; k < 70; k++)
                canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,-7 L1.6,-1.6 L7,0 L1.6,1.6 L0,7 L-1.6,1.6 L-7,0 L-1.6,-1.6 Z"), Fill = Brush(Alpha(Colors.White, (byte)(120 + rnd.Next(120)))),
                    RenderTransform = new ScaleTransform(.6 + rnd.NextDouble(), .6 + rnd.NextDouble()) }, 40 + (rnd.NextDouble() * (w - 80)), 40 + (rnd.NextDouble() * (h - 80))));
        }
        else if (!common)
            canvas.Children.Add(new Path { Data = RoundRect(0, 0, w, h, 38, 38, 38, 38), Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Alpha(C("#FFFFFF"), 0), .25), new(Alpha(legendary ? C("#FFE6A0") : C("#CFE8FF"), 46), .45), new(Alpha(C("#FFFFFF"), 0), .62),
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
        var face = d.UserFace;
        var rarity = d.Text("rarity");
        var r = rarity.ToLowerInvariant();
        var tier = r.Contains("legend") ? 3 : r.Contains("epic") ? 2 : r.Contains("rare") ? 1 : 0;
        var (glow, metal, panelTop, panelBottom) = tier switch
        {
            3 => (C("#F29A2E"), C("#E0B85A"), C("#2E2236"), C("#120A14")),
            2 => (C("#B266F2"), C("#C9A8F2"), C("#26183F"), C("#0E081C")),
            1 => (C("#4C9AF0"), C("#C3D2E6"), C("#1A2A4A"), C("#0A1226")),
            _ => (C("#C2C6CE"), C("#8D9098"), C("#262830"), C("#111216")),
        };
        Brush Metal() => new LinearGradientBrush(new GradientStopCollection { new(Lighten(metal, .5), 0), new(metal, .35), new(Darken(metal, .55), .6), new(Lighten(metal, .2), .85), new(Darken(metal, .6), 1) }, new Point(0, 0), new Point(1, 1));
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(tier == 0 ? 6 : 18),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Lighten(panelTop, .06), 0), new(panelTop, .45), new(panelBottom, 1) }, 90),
            BorderBrush = Metal(), BorderThickness = new Thickness(6 + (tier * 2)) });
        canvas.Children.Add(new Border { Width = w, Height = h, Background = Fibers(Alpha(Colors.White, 0), Alpha(Colors.White, 10), 1.4) });
        canvas.Children.Add(At(new Border { Width = w - 40, Height = h - 40, CornerRadius = new CornerRadius(tier == 0 ? 2 : 10), BorderBrush = Brush(Alpha(metal, 170)), BorderThickness = new Thickness(2) }, 20, 20));
        if (tier >= 2)
            canvas.Children.Add(At(new Border { Width = w - 56, Height = h - 56, CornerRadius = new CornerRadius(8), BorderBrush = Brush(Alpha(glow, 90)), BorderThickness = new Thickness(1) }, 28, 28));
        // Corners: rivets on common gear, scrolls above it, a set gem on epic and legendary.
        foreach (var (x, y, a) in new[] { (20.0, 20.0, 0.0), (w - 20, 20.0, 90.0), (w - 20, h - 20, 180.0), (20.0, h - 20, 270.0) })
        {
            if (tier == 0)
            {
                canvas.Children.Add(At(new Ellipse { Width = 16, Height = 16, Fill = new RadialGradientBrush(Lighten(metal, .5), Darken(metal, .5)) }, x + (a is 90 or 180 ? -30 : 14), y + (a is 180 or 270 ? -30 : 14)));
                continue;
            }
            var size = tier == 3 ? 1.35 : 1.0;
            canvas.Children.Add(At(new Path { Data = Geometry.Parse("M0,0 L70,0 C50,6 40,14 34,34 C14,40 6,50 0,70 Z M14,14 m-6,0 a6,6 0 1,0 12,0 a6,6 0 1,0 -12,0"),
                Fill = Metal(), RenderTransform = new TransformGroup { Children = { new ScaleTransform(size, size), new RotateTransform(a) } } }, x, y));
            if (tier >= 2)
            {
                var gx = x + ((a is 90 or 180 ? -1 : 1) * 30 * size) - 9;
                var gy = y + ((a is 180 or 270 ? -1 : 1) * 30 * size) - 9;
                canvas.Children.Add(At(new Path { Data = Geometry.Parse("M9,0 L18,9 L9,18 L0,9 Z"), Fill = new LinearGradientBrush(Lighten(glow, .5), Darken(glow, .6), 90),
                    Effect = new DropShadowEffect { Color = glow, BlurRadius = 12, ShadowDepth = 0, Opacity = .9 } }, gx, gy));
            }
        }
        // Title and tabs.
        var heading = Spaced("INVENTORY", DmSerif, 46, FontWeights.Normal, metal, TextAlignment.Left, .3);
        if (tier == 3)
            heading.Foreground = Metal();
        canvas.Children.Add(At(heading, 70, 58));
        var gold = Label("♪ 027 G", Vt323, 40, FontWeights.Normal, C("#E0B85A"), TextAlignment.Right);
        gold.Width = 230;
        canvas.Children.Add(At(gold, w - 300, 64));
        var tabs = new[] { "Items", "Key Items", "Songs" };
        for (var i = 0; i < tabs.Length; i++)
        {
            var active = i == 2;
            var tab = new Border { Width = 200, Height = 54, CornerRadius = new CornerRadius(10, 10, 0, 0),
                Background = active ? new LinearGradientBrush(Lighten(panelTop, .14), panelTop, 90) : Brush(Alpha(Colors.Black, 60)),
                BorderBrush = Brush(Alpha(metal, (byte)(active ? 220 : 90))), BorderThickness = new Thickness(2, 2, 2, 0),
                Child = Label(tabs[i], Georgia, 24, active ? FontWeights.Bold : FontWeights.Normal, active ? C("#F4E7C4") : C("#8A93B8"), TextAlignment.Center) };
            ((FrameworkElement)tab.Child).VerticalAlignment = VerticalAlignment.Center;
            canvas.Children.Add(At(tab, 70 + (i * 212), 140));
        }
        canvas.Children.Add(At(new Border { Width = w - 140, Height = 2, Background = Brush(Alpha(metal, 200)) }, 70, 194));
        // Slots; the song's glows in its rarity, with light rising from it on the rarest.
        string[] icons = ["⚗", "⚔", "✦", "☘", "✉", "♛", "♬", "☕", "❂", "☾"];
        var rnd = new Random(42);
        for (var i = 0; i < 10; i++)
        {
            var x = 70 + ((i % 5) * 176);
            var y = 224 + ((i / 5) * 176);
            var selected = i == 6;
            var slot = new Border { Width = 156, Height = 156, CornerRadius = new CornerRadius(tier == 0 ? 4 : 12),
                Background = selected ? new RadialGradientBrush(Alpha(glow, (byte)(60 + (tier * 30))), Alpha(Colors.Black, 70)) : new LinearGradientBrush(Alpha(Colors.White, 14), Alpha(Colors.Black, 70), 90),
                BorderBrush = selected ? Metal() : Brush(Alpha(metal, 90)), BorderThickness = new Thickness(selected ? 5 : 2),
                Child = Label(icons[i], Symbols, 64, FontWeights.Normal, selected ? Lighten(glow, .35) : C("#8A93B8"), TextAlignment.Center) };
            ((FrameworkElement)slot.Child).VerticalAlignment = VerticalAlignment.Center;
            if (selected)
                slot.Effect = new DropShadowEffect { Color = glow, BlurRadius = 20 + (tier * 10), ShadowDepth = 0, Opacity = .6 + (tier * .12) };
            canvas.Children.Add(At(slot, x, y));
            if (selected && tier >= 2)
                for (var k = 0; k < 10 + (tier * 8); k++)
                    canvas.Children.Add(At(new Ellipse { Width = 4 + rnd.Next(5), Height = 4 + rnd.Next(5), Fill = Brush(Alpha(Lighten(glow, .5), (byte)(120 + rnd.Next(130)))) },
                        x + 10 + (rnd.NextDouble() * 136), y - 30 + (rnd.NextDouble() * 160)));
            if (selected && tier == 3)
                canvas.Children.Add(At(new Border { Width = 156, Height = 120, Background = new LinearGradientBrush(Alpha(glow, 0), Alpha(glow, 70), 90), IsHitTestVisible = false }, x, y - 110));
            if (i is 0 or 3 or 8)
                canvas.Children.Add(At(Label("x" + (i + 2), Vt323, 28, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Right), x + 96, y + 116));
        }
        canvas.Children.Add(At(Label("▼", Symbols, 26, FontWeights.Normal, glow, TextAlignment.Left), 70 + 176 + 64, 560));
        // Detail pane: header, the lines centred in the space they have, stats and lore at the foot.
        var pane = new Border { Width = w - 140, Height = 640, CornerRadius = new CornerRadius(tier == 0 ? 4 : 14), Background = Brush(Alpha(Colors.Black, 80)),
            BorderBrush = Brush(Alpha(metal, 140)), BorderThickness = new Thickness(2), Padding = new Thickness(40, 30, 40, 28) };
        var detail = new Grid();
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition());
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel();
        var title = Label(d.Title, DmSerif, 50, FontWeights.Normal, Lighten(glow, .25), TextAlignment.Left);
        if (tier == 3)
            title.Foreground = new LinearGradientBrush(C("#FFE9A8"), C("#F29A2E"), 90);
        title.Effect = new DropShadowEffect { Color = glow, BlurRadius = 8 + (tier * 6), ShadowDepth = 0, Opacity = .4 + (tier * .15) };
        head.Children.Add(title);
        var meta = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var stars = Label(d.Text("rating"), Symbols, 30, FontWeights.Normal, C("#E0B85A"), TextAlignment.Right);
        DockPanel.SetDock(stars, Dock.Right);
        meta.Children.Add(stars);
        var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 3, 12, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(Lighten(glow, .1), Darken(glow, .6), 90), BorderBrush = Brush(Lighten(glow, .5)), BorderThickness = new Thickness(1),
            Child = Spaced((rarity + " · " + d.Text("item")).ToUpperInvariant(), Figtree, 18, FontWeights.Bold, tier == 0 ? C("#1A1A1A") : Colors.White, TextAlignment.Left, .25) };
        meta.Children.Add(badge);
        head.Children.Add(meta);
        head.Children.Add(new Border { Height = 1.5, Background = Brush(Alpha(metal, 120)), Margin = new Thickness(0, 18, 0, 0) });
        detail.Children.Add(head);
        var lore = LyricLines(d.LyricText, face?.Family ?? Georgia, face?.Weight ?? FontWeights.Normal, C("#F1E9D2"), w - 220, 300, Presence(d, 52), 24,
            d.Align(TextAlignment.Left), Leading(d, 1.24), gap: 4, wholeShrink: .7, italic: face is null);
        lore.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(lore, 1);
        detail.Children.Add(lore);
        var foot = new StackPanel();
        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(Label("VALUE      " + d.Text("value"), Vt323, 32, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Left));
        left.Children.Add(Label("TRADEABLE  " + d.Text("trade"), Vt323, 32, FontWeights.Normal, C("#C9CFEA"), TextAlignment.Left));
        stats.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(Label("+" + (25 + (tier * 25)) + " Nostalgia", Vt323, 32, FontWeights.Normal, C("#7FE07A"), TextAlignment.Left));
        right.Children.Add(Label("+" + (d.Timestamp ? d.Time : "27") + " Replay", Vt323, 32, FontWeights.Normal, C("#7FE07A"), TextAlignment.Left));
        Grid.SetColumn(right, 1);
        stats.Children.Add(right);
        foot.Children.Add(stats);
        var note = Label("“" + d.Text("note") + "”", Georgia, 24, FontWeights.Normal, C("#9AA3C6"), TextAlignment.Left);
        note.FontStyle = FontStyles.Italic;
        note.Margin = new Thickness(0, 14, 0, 0);
        foot.Children.Add(note);
        Grid.SetRow(foot, 2);
        detail.Children.Add(foot);
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
        return ObjectStage(d, canvas, w, h, Desk(Lighten(Darken(glow, .35), .12), C("#0D0B18")), C("#E8DDBE"), C("#0D0B18"));
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
        var doodle2 = d.Color("doodle2", C("#FFB000"));
        var paperColor = d.Color("canvas", Colors.White);
        var face = d.UserFace;
        var body = new Grid { Width = w - 20 };
        for (var r = 0; r < 4; r++)
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
            if (i == 1 || i == 3)
                icon.Fill = Brush(C("#C0C0C0"));
            RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
            grid.Children.Add(Bevel(new Grid { Width = 34, Height = 34, Children = { icon } }, 1, raised: i != 8, margin: new Thickness(1), padding: 4));
        }
        toolbox.Children.Add(grid);
        toolbox.Children.Add(Bevel(new StackPanel
        {
            Width = 88, Height = 120,
            Children =
            {
                new Border { Height = 2, Margin = new Thickness(10, 22, 10, 0), Background = Brush(Colors.Black) },
                new Border { Height = 4, Margin = new Thickness(10, 18, 10, 0), Background = Brush(Colors.Black) },
                new Border { Height = 6, Margin = new Thickness(10, 18, 10, 0), Background = Brush(C("#000080")) },
            },
        }, 1, raised: false, margin: new Thickness(4, 10, 4, 0), padding: 0));
        work.Children.Add(toolbox);
        var well = new Grid { Background = Brush(C("#808080")) };
        var sheet = new Grid { Width = 800, Height = 760, Background = Brush(paperColor), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        RenderOptions.SetEdgeMode(sheet, EdgeMode.Aliased);
        TextOptions.SetTextRenderingMode(sheet, TextRenderingMode.Aliased);
        var art = new Canvas();
        // The decoration behind everything: spray, a rainbow, clouds or sparkles.
        var deco = d.Text("deco");
        var rnd = new Random(98);
        switch (deco)
        {
            case "Spray":
                for (var i = 0; i < 160; i++)
                {
                    var a = rnd.NextDouble() * Math.PI * 2;
                    var rr = Math.Sqrt(rnd.NextDouble()) * 74;
                    art.Children.Add(At(new Rectangle { Width = 3, Height = 3, Fill = Brush(i % 3 == 0 ? doodle2 : doodle) }, 680 + (Math.Cos(a) * rr), 690 + (Math.Sin(a) * rr * .7)));
                }
                break;
            case "Rainbow":
                string[] bands = ["#FF0000", "#FF8000", "#FFFF00", "#00C000", "#0000FF", "#8000FF"];
                for (var i = 0; i < bands.Length; i++)
                    art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M0,{200 - (i * 14)} A{200 - (i * 14)},{200 - (i * 14)} 0 0 0 {200 - (i * 14)},0")), C(bands[i]), 14));
                break;
            case "Clouds":
                foreach (var (x, y, s) in new[] { (560.0, 60.0, 1.0), (60.0, 600.0, .8), (610.0, 640.0, .7) })
                {
                    var cloud = Geometry.Parse(FormattableString.Invariant($"M{x},{y + (40 * s)} a{30 * s},{30 * s} 0 0 1 {40 * s},{-36 * s} a{36 * s},{36 * s} 0 0 1 {66 * s},{-6 * s} a{30 * s},{30 * s} 0 0 1 {50 * s},{42 * s} Z"));
                    art.Children.Add(new Path { Data = cloud, Fill = Brush(Colors.White), Stroke = Brush(C("#0080FF")), StrokeThickness = 3 });
                    RenderOptions.SetEdgeMode(art.Children[^1], EdgeMode.Aliased);
                }
                break;
            case "Sparkles":
                for (var i = 0; i < 26; i++)
                {
                    var x = 30 + (rnd.NextDouble() * 740);
                    var y = 30 + (rnd.NextDouble() * 700);
                    if (x > 80 && x < 720 && y > 140 && y < 600)
                        continue;
                    var s = 6 + rnd.Next(10);
                    art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M{x - s},{y} L{x + s},{y} M{x},{y - s} L{x},{y + s}")), i % 2 == 0 ? doodle : doodle2, 3));
                }
                break;
        }
        // Doodles drawn with the pencil.
        var shape = d.Text("shape");
        var spots = new[] { (640.0, 80.0, 1.2), (700.0, 600.0, .9), (90.0, 640.0, .7), (110.0, 80.0, .6) };
        for (var i = 0; i < spots.Length; i++)
        {
            var (x, y, s) = spots[i];
            var color = i % 2 == 0 ? doodle : doodle2;
            if (deco == "Rainbow" && i == 3)
                continue;
            switch (shape)
            {
                case "Stars":
                    art.Children.Add(PixelStroke(StarPath(x, y, 34 * s), color, 5));
                    break;
                case "Hearts":
                    art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M{x},{y + (30 * s)} C{x - (40 * s)},{y} {x - (24 * s)},{y - (30 * s)} {x},{y - (10 * s)} C{x + (24 * s)},{y - (30 * s)} {x + (40 * s)},{y} {x},{y + (30 * s)} Z")), color, 5));
                    break;
                case "Flowers":
                    for (var p = 0; p < 5; p++)
                    {
                        var a = p * Math.PI * 2 / 5;
                        art.Children.Add(PixelStroke(new EllipseGeometry(new Point(x + (Math.Cos(a) * 18 * s), y + (Math.Sin(a) * 18 * s)), 13 * s, 13 * s), color, 4));
                    }
                    art.Children.Add(PixelStroke(new EllipseGeometry(new Point(x, y), 8 * s, 8 * s), doodle2, 6));
                    art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M{x},{y + (30 * s)} C{x + (6 * s)},{y + (60 * s)} {x - (6 * s)},{y + (80 * s)} {x + (4 * s)},{y + (100 * s)}")), C("#008000"), 4));
                    break;
                case "Notes":
                    art.Children.Add(new Path { Data = new EllipseGeometry(new Point(x - (12 * s), y + (30 * s)), 14 * s, 10 * s), Fill = Brush(color) });
                    art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M{x + (1 * s)},{y + (30 * s)} L{x + (1 * s)},{y - (34 * s)} C{x + (14 * s)},{y - (22 * s)} {x + (30 * s)},{y - (20 * s)} {x + (26 * s)},{y}")), color, 5));
                    break;
                case "Sun":
                    if (i == 0)
                    {
                        art.Children.Add(new Path { Data = new EllipseGeometry(new Point(x + 40, y), 46, 46), Fill = Brush(doodle2), Stroke = Brush(doodle), StrokeThickness = 4 });
                        for (var p = 0; p < 12; p++)
                        {
                            var a = p * Math.PI / 6;
                            art.Children.Add(PixelStroke(new LineGeometry(new Point(x + 40 + (Math.Cos(a) * 58), y + (Math.Sin(a) * 58)), new Point(x + 40 + (Math.Cos(a) * 86), y + (Math.Sin(a) * 86))), doodle, 5));
                        }
                    }
                    break;
                case "Scribbles":
                    var pts = string.Join(" ", Enumerable.Range(0, 14).Select(k => FormattableString.Invariant($"{x - (40 * s) + (k * 6 * s) + (rnd.NextDouble() * 10)},{y + ((k % 2 == 0 ? -1 : 1) * (14 + (rnd.NextDouble() * 18)) * s)}")));
                    art.Children.Add(PixelStroke(Geometry.Parse("M" + pts.Replace(" ", " L")), color, 4));
                    break;
            }
        }
        var lyric = LyricLines(d.LyricText, face?.Family ?? Arial, face?.Weight ?? FontWeights.Bold, ink, 600, 430, Presence(d, 60), 26,
            d.Align(TextAlignment.Left), Leading(d, 1.1), gap: 4, wholeShrink: .6);
        lyric.Measure(new Size(600, double.PositiveInfinity));
        var lh = lyric.DesiredSize.Height;
        var top = Math.Max(150, ((760 - lh) / 2) - 30);
        art.Children.Add(At(lyric, 100, top));
        art.Children.Add(At(new Rectangle { Width = 640, Height = lh + 40, Stroke = Brush(Colors.Black), StrokeThickness = 1.5, StrokeDashArray = [4, 3] }, 80, top - 20));
        foreach (var (hx, hy) in new[] { (80.0, top - 20), (400.0, top - 20), (720.0, top - 20), (80.0, top + lh + 20), (400.0, top + lh + 20), (720.0, top + lh + 20), (80.0, top + (lh / 2)), (720.0, top + (lh / 2)) })
            art.Children.Add(At(new Rectangle { Width = 9, Height = 9, Fill = Brush(C("#000080")) }, hx - 4, hy - 4));
        if (shape == "Underline")
            art.Children.Add(PixelStroke(Geometry.Parse(FormattableString.Invariant($"M110,{top + lh + 50} C260,{top + lh + 70} 480,{top + lh + 36} 690,{top + lh + 58}")), doodle, 6));
        art.Children.Add(At(Label("- " + d.Title + ", " + d.Artist, Arial, 22, FontWeights.Normal, Darken(ink, .7), TextAlignment.Left), 100, 700));
        sheet.Children.Add(art);
        well.Children.Add(sheet);
        var wellBox = Bevel(well, 1, raised: false, padding: 0);
        Grid.SetColumn(wellBox, 1);
        work.Children.Add(wellBox);
        body.Children.Add(work);
        string[] palette =
        [
            "#000000", "#808080", "#800000", "#808000", "#008000", "#008080", "#000080", "#800080", "#808040", "#004040", "#0080FF", "#004080", "#4000FF", "#804000",
            "#FFFFFF", "#C0C0C0", "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FFFF80", "#00FF80", "#80FFFF", "#8080FF", "#FF0080", "#FF8040",
        ];
        var paletteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 0, 6) };
        var current = new Grid { Width = 60, Height = 60, Margin = new Thickness(0, 0, 10, 0) };
        current.Children.Add(At(new Border { Width = 34, Height = 34, Background = Brush(doodle), BorderBrush = Brush(C("#808080")), BorderThickness = new Thickness(2) }, 22, 22));
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

    private static Color Mid(Color a, Color b) => Color.FromArgb((byte)((a.A + b.A) / 2), (byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    /// <summary>Seventies label stripes: orange, yellow, red, brown and teal bands.</summary>
    private static Brush RetroStripes(double angle) => new LinearGradientBrush(new GradientStopCollection
    {
        new(C("#E8572A"), 0), new(C("#E8572A"), .2), new(C("#F29E38"), .2), new(C("#F29E38"), .4), new(C("#F7C948"), .4), new(C("#F7C948"), .6),
        new(C("#6BAF92"), .6), new(C("#6BAF92"), .8), new(C("#3E7CB1"), .8), new(C("#3E7CB1"), 1),
    }, angle);

    /// <summary>A holographic foil's rainbow, at the given strength.</summary>
    private static Brush Rainbow(double strength)
    {
        byte A(double v) => (byte)(v * strength * 255);
        return new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(A(.0), 255, 120, 200), 0), new(Color.FromArgb(A(.5), 255, 120, 200), .15), new(Color.FromArgb(A(.5), 255, 230, 120), .32),
            new(Color.FromArgb(A(.45), 120, 255, 170), .5), new(Color.FromArgb(A(.5), 110, 190, 255), .68), new(Color.FromArgb(A(.5), 190, 130, 255), .85), new(Color.FromArgb(A(0), 255, 255, 255), 1),
        }, new Point(0, 0), new Point(1, 1));
    }

    /// <summary>Faint veining over a card frame, like printed stone.</summary>
    private static Brush Marble(Color frame)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 400, 400))));
        var pen = new System.Windows.Media.Pen(Brush(Alpha(Lighten(frame, .6), 40)), 2);
        g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M0,60 C80,40 120,120 200,90 S330,30 400,80 M0,250 C90,230 140,300 230,270 S340,220 400,260 M60,0 C90,80 40,140 90,210 M300,400 C280,330 340,300 320,220")));
        var brush = new DrawingBrush(g) { TileMode = TileMode.FlipXY, Viewport = new Rect(0, 0, 400, 400), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
}
