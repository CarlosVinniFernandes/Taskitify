using Avalonia;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace Taskitify.Linux;

/// <summary>Player glyphs as vector paths: no dependency on which symbol fonts the system has.</summary>
internal static class Icons
{
    public const string Play = "M8,5 L19,12 L8,19 Z";
    public const string Pause = "M6,5 H10 V19 H6 Z M14,5 H18 V19 H14 Z";
    public const string Next = "M6,5 L15,12 L6,19 Z M17,5 H20 V19 H17 Z";
    public const string Previous = "M18,5 L9,12 L18,19 Z M4,5 H7 V19 H4 Z";
    public const string Shuffle = "M4,6 H8 L16,18 H20 M4,18 H8 L10,15 M14,9 L16,6 H20 M17,3 L20,6 L17,9 M17,15 L20,18 L17,21";
    public const string Repeat = "M4,11 V9 A3,3 0 0 1 7,6 H18 M15,3 L18,6 L15,9 M20,13 V15 A3,3 0 0 1 17,18 H6 M9,15 L6,18 L9,21";

    public static Path Make(string data, IBrush brush, double size = 16, bool stroke = false) => new()
    {
        Data = Geometry.Parse(data),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Fill = stroke ? null : brush,
        Stroke = stroke ? brush : null,
        StrokeThickness = stroke ? 1.8 : 0,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    };
}
