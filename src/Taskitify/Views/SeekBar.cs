using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskitify.Views;

/// <summary>
/// Thin progress bar for the mini player. Press anywhere on it to jump there and keep the
/// button down to drag; the song only seeks once, on release. While the user drags, outside
/// updates of <see cref="Value"/> are kept aside so the playing song cannot pull the bar back.
/// </summary>
/// <remarks>
/// Replaces a templated Slider, whose move-to-point click marked the event handled (so a
/// click on the track never counted as a drag), only dragged from its thumb and left a gap
/// in the line where its hidden thumb sat.
/// </remarks>
public sealed class SeekBar : Grid
{
    private const double ThumbSize = 12;
    private const double LineHeight = 4;

    private readonly Border _fill;
    private readonly Ellipse _thumb;
    private readonly TranslateTransform _thumbOffset = new();
    private double _value;
    private double _maximum = 1;
    private double _dragValue;

    public SeekBar()
    {
        Height = 16;
        Background = Brushes.Transparent; // hit-testable between and around the lines
        Cursor = Cursors.Hand;
        Focusable = false;

        var track = new Border { Height = LineHeight, CornerRadius = new CornerRadius(LineHeight / 2), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        _fill = new Border
        {
            Height = LineHeight,
            CornerRadius = new CornerRadius(LineHeight / 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        _fill.SetResourceReference(Border.BackgroundProperty, "TextPrimaryBrush");
        _thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _thumbOffset,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _thumb.SetResourceReference(Shape.FillProperty, "TextPrimaryBrush");
        Children.Add(track);
        Children.Add(_fill);
        Children.Add(_thumb);

        SizeChanged += (_, _) => Render();
        IsEnabledChanged += (_, _) =>
        {
            Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
            Render();
        };
    }

    /// <summary>The user picked a position (seconds) and let go.</summary>
    public event Action<double>? Seek;

    /// <summary>The position under the pointer while dragging (seconds), or null once the drag ends.</summary>
    public event Action<double?>? Preview;

    public bool IsDragging { get; private set; }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(0, value);
            Render();
        }
    }

    /// <summary>The playing position (seconds). Shown again once a drag ends.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            if (!IsDragging)
                Render();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled || _maximum <= 0 || !CaptureMouse())
            return;
        IsDragging = true;
        e.Handled = true;
        DragTo(e.GetPosition(this).X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging)
            DragTo(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsDragging)
            return;
        e.Handled = true;
        DragTo(e.GetPosition(this).X);
        // Before releasing capture, so OnLostMouseCapture does not treat it as a cancel.
        IsDragging = false;
        ReleaseMouseCapture();
        _value = _dragValue;
        Render();
        Preview?.Invoke(null);
        Seek?.Invoke(_dragValue);
    }

    /// <summary>Capture taken away mid-drag (popup hidden, Esc, another window): no seek.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsDragging)
            return;
        IsDragging = false;
        Render();
        Preview?.Invoke(null);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Render();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Render();
    }

    private void DragTo(double x)
    {
        var width = ActualWidth;
        var fraction = width > 0 ? Math.Clamp(x / width, 0, 1) : 0;
        _dragValue = fraction * _maximum;
        Render();
        Preview?.Invoke(_dragValue);
    }

    private void Render()
    {
        var width = ActualWidth;
        var shown = IsDragging ? _dragValue : _value;
        var fraction = _maximum > 0 ? Math.Clamp(shown / _maximum, 0, 1) : 0;
        _fill.Width = fraction * width;
        _thumbOffset.X = (fraction * width) - (ThumbSize / 2);
        _thumb.Opacity = IsEnabled && (IsDragging || IsMouseOver) ? 1 : 0;
    }
}
