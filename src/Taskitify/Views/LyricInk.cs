using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Taskitify.Core.Lyrics;

namespace Taskitify.Views;

/// <summary>Uses WPF's own caret geometry so the dim and lit ink share the exact shaped text.</summary>
internal sealed class LyricInk(TextBlock text)
{
    private static readonly Brush DimInk = Freeze(new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)));
    private const double Overhang = 1.25;
    private DrawingBrush? _mask;
    private Cell[] _cells = [];
    private Row[] _rows = [];
    private double _distance;
    private int _filledRow = -1;
    private Size _size;
    private DpiScale _dpi;
    private string? _text;

    private readonly record struct Cell(int Start, int End, int Row, double From, double To, double Distance);
    private sealed record Row(double From, double To, Rect Bounds, RectangleGeometry Lit);

    public void Clear() => text.ClearValue(UIElement.OpacityMaskProperty);

    public void Show(LyricFill fill)
    {
        if (fill.IsComplete)
        {
            Clear();
            return;
        }
        if (!text.IsMeasureValid || !text.IsArrangeValid)
            return;
        var dpi = VisualTreeHelper.GetDpi(text);
        if (_size != text.RenderSize || _text != text.Text || _dpi.DpiScaleX != dpi.DpiScaleX || _dpi.DpiScaleY != dpi.DpiScaleY)
            Rebuild(dpi);
        if (_mask is null)
        {
            Clear();
            return;
        }

        var target = fill.ByText ? fill.Value : fill.Value * _distance;
        var i = FindCell(target, fill.ByText);
        var cell = _cells[i];
        var fraction = fill.ByText
            ? (target - cell.Start) / (cell.End - cell.Start)
            : (target - cell.Distance) / Math.Abs(cell.To - cell.From);
        var row = _rows[cell.Row];
        var edge = cell.From + Math.Clamp(fraction, 0, 1) * (cell.To - cell.From);
        var first = Math.Max(0, Math.Min(_filledRow, cell.Row));
        var last = Math.Max(_filledRow, cell.Row);
        for (var j = first; j <= last; j++)
        {
            var item = _rows[j];
            var bounds = j < cell.Row ? item.Bounds : j > cell.Row ? Rect.Empty : LitRect(row, edge);
            if (item.Lit.Rect != bounds)
                item.Lit.Rect = bounds;
        }
        _filledRow = cell.Row;
        if (text.OpacityMask != _mask)
            text.OpacityMask = _mask;
    }

    private static Rect LitRect(Row row, double edge)
    {
        if (Math.Abs(edge - row.From) < 0.01)
            return Rect.Empty;
        return row.To >= row.From
            ? new Rect(row.Bounds.Left, row.Bounds.Top, Math.Max(0, edge - row.Bounds.Left), row.Bounds.Height)
            : new Rect(edge, row.Bounds.Top, Math.Max(0, row.Bounds.Right - edge), row.Bounds.Height);
    }

    private int FindCell(double target, bool byText)
    {
        var start = 0;
        var end = _cells.Length - 1;
        while (start < end)
        {
            var middle = (start + end + 1) / 2;
            var value = byText ? _cells[middle].Start : _cells[middle].Distance;
            if (value <= target)
                start = middle;
            else
                end = middle - 1;
        }
        return start;
    }

    private void Rebuild(DpiScale dpi)
    {
        _text = text.Text;
        _dpi = dpi;
        _size = text.RenderSize;
        _distance = 0;
        _filledRow = -1;
        _mask = null;
        if (_size.Width <= 0 || _size.Height <= 0 || string.IsNullOrEmpty(_text))
            return;

        var origin = text.ContentStart;
        while (origin.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
            && origin.GetNextContextPosition(LogicalDirection.Forward) is { } next)
            origin = next;

        var offsets = StringInfo.ParseCombiningCharacters(_text);
        var rows = new List<Row>();
        var cells = new List<Cell>();
        for (var i = 0; i < offsets.Length; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Length ? offsets[i + 1] : _text.Length;
            var a = origin.GetPositionAtOffset(start, LogicalDirection.Forward)?.GetCharacterRect(LogicalDirection.Forward) ?? Rect.Empty;
            var b = origin.GetPositionAtOffset(end, LogicalDirection.Backward)?.GetCharacterRect(LogicalDirection.Backward) ?? Rect.Empty;
            if (a.IsEmpty || b.IsEmpty || a.Top >= _size.Height || Math.Abs(a.Top - b.Top) > 1 || Math.Abs(a.X - b.X) < 0.01)
                continue;
            var left = Math.Min(a.X, b.X);
            var right = Math.Max(a.X, b.X);
            if (right <= 0 || left >= _size.Width)
                continue;
            var bounds = new Rect(Math.Max(0, left - Overhang), Math.Max(0, a.Top - 0.5),
                Math.Min(_size.Width, right + Overhang) - Math.Max(0, left - Overhang), Math.Max(a.Height, b.Height) + 1);
            if (rows.Count > 0 && Math.Abs(rows[^1].Bounds.Top - bounds.Top) < 1 && Math.Abs(rows[^1].To - a.X) < 1
                && Math.Sign(rows[^1].To - rows[^1].From) == Math.Sign(b.X - a.X))
            {
                var row = rows[^1];
                rows[^1] = row with { To = b.X, Bounds = Rect.Union(row.Bounds, bounds) };
            }
            else
                rows.Add(new Row(a.X, b.X, bounds, new RectangleGeometry(Rect.Empty)));
            cells.Add(new Cell(start, end, rows.Count - 1, a.X, b.X, _distance));
            _distance += Math.Abs(b.X - a.X);
        }
        if (cells.Count == 0)
            return;
        _rows = rows.ToArray();
        _cells = cells.ToArray();
        var frame = new Rect(_size);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(DimInk, null, Freeze(new RectangleGeometry(frame))));
        foreach (var row in _rows)
            drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, row.Lit));
        _mask = new DrawingBrush(drawing)
        {
            Viewbox = frame, ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = frame, ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}

/// <summary>A redraw request is clock-driven and expires as soon as the current verse is fully lit.</summary>
internal sealed class LyricInkDriver
{
    private readonly DispatcherTimer _timer;
    private readonly Func<(TextBlock? Text, LyricFill Fill, bool Playing)> _sample;
    private TextBlock? _text;
    private LyricInk? _ink;

    public LyricInkDriver(TimeSpan interval, Func<(TextBlock?, LyricFill, bool)> sample)
    {
        _sample = sample;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = interval };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var (text, fill, playing) = _sample();
        if (text != _text)
        {
            Invalidate();
            _text = text;
            if (text is not null)
            {
                text.SizeChanged += OnSizeChanged;
                _ink = new LyricInk(text);
            }
        }
        _ink?.Show(fill);
        if (text is not null && playing && !fill.IsComplete)
            _timer.Start();
        else
            _timer.Stop();
    }

    public void Invalidate()
    {
        if (_text is not null)
            _text.SizeChanged -= OnSizeChanged;
        _ink?.Clear();
        _text = null;
        _ink = null;
        _timer.Stop();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();
}
