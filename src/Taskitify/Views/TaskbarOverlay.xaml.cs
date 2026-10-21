using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Taskitify.Infrastructure;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>
/// The strip drawn over the weather widget. Positioning, z-order and visibility are owned by
/// TaskbarOverlayService; this class only renders and reports input.
/// </summary>
public partial class TaskbarOverlay : Window
{
    private const double MarqueeSpeed = 40; // DIP per second
    private static readonly TimeSpan MarqueePause = TimeSpan.FromSeconds(1.2);
    private const double ResizeGrip = 8; // DIP at each side that resize while calibrating

    private readonly TaskbarViewModel _vm;
    private TextBlock _front;
    private TextBlock _back;
    private double _textLift;
    private int _resizeEdge; // -1 left, 1 right, 0 no side drag
    private int _resizeStartX; // physical pixels

    public TaskbarOverlay(TaskbarViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _front = LineA;
        _back = LineB;
        _front.Text = vm.MainText;
        vm.PropertyChanged += OnViewModelChanged;
        VolumeButton.PreviewMouseWheel += (_, e) =>
        {
            _vm.NudgeVolume(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
        VolumeButton.MouseEnter += (_, _) => _vm.RefreshVolume();
        LineHost.SizeChanged += (_, _) => UpdateMarquee();
        TextHost.SizeChanged += (_, _) => UpdateTextLift();
        IsVisibleChanged += (_, _) => UpdateMarquee();
        ApplyBackground(animate: false);
        UpdateLineLayout();
    }

    /// <summary>Plain left click (not in calibration, not on a control).</summary>
    public event EventHandler? Clicked;

    /// <summary>Calibration drag finished; the service persists the new bounds.</summary>
    public event EventHandler? CalibrationBoundsChanged;

    /// <summary>Wheel delta while calibrating: positive = wider.</summary>
    public event EventHandler<int>? CalibrationResize;

    /// <summary>A side dragged while calibrating (-1 left, 1 right) and how far the cursor is from where it began, in physical pixels.</summary>
    public event EventHandler<(int Edge, int Offset)>? CalibrationEdgeDrag;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        HoverPlate.Background = _vm.Hover;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HoverPlate.Background = Brushes.Transparent;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm.IsCalibrating)
        {
            try
            {
                DragMove(); // modal until the button is released
            }
            catch (InvalidOperationException)
            {
            }
            CalibrationBoundsChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Buttons mark their clicks handled, so this only fires for the cover/text area.
        if (!_vm.IsCalibrating)
            Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_vm.IsCalibrating)
            CalibrationResize?.Invoke(this, e.Delta > 0 ? 1 : -1);
    }

    // Calibration: the sides resize like a window's borders, the middle moves (OnMouseLeftButtonDown).
    // Preview events, so a button at a side does not take the press.
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_resizeEdge != 0)
        {
            // The real cursor, not the event's point: the window moves under it when the left side is dragged.
            if (NativeMethods.GetCursorPos(out var p))
                CalibrationEdgeDrag?.Invoke(this, (_resizeEdge, p.X - _resizeStartX));
        }
        else if (_vm.IsCalibrating)
        {
            var side = EdgeAt(e.GetPosition(this).X) != 0;
            Cursor = side ? Cursors.SizeWE : Cursors.SizeAll;
            ForceCursor = side; // over the buttons' hand cursor too
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!_vm.IsCalibrating)
            return;
        var edge = EdgeAt(e.GetPosition(this).X);
        // Without the capture the release could be missed outside the window, leaving the side stuck to the cursor.
        if (edge == 0 || !NativeMethods.GetCursorPos(out var p) || !CaptureMouse())
            return;
        _resizeEdge = edge;
        _resizeStartX = p.X;
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_resizeEdge != 0)
        {
            ReleaseMouseCapture(); // finishes in OnLostMouseCapture
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_resizeEdge == 0)
            return;
        _resizeEdge = 0;
        CalibrationBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>-1 over the left side, 1 over the right one, 0 elsewhere. A narrow strip keeps a middle to drag.</summary>
    private int EdgeAt(double x)
    {
        var grip = Math.Min(ResizeGrip, ActualWidth / 4);
        return x <= grip ? -1 : x >= ActualWidth - grip ? 1 : 0;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TaskbarViewModel.MainText):
                ShowLine(_vm.MainText);
                break;
            case nameof(TaskbarViewModel.GradientStart):
            case nameof(TaskbarViewModel.GradientEnd):
                ApplyBackground(animate: _vm.EnableAnimations && IsVisible);
                break;
            case nameof(TaskbarViewModel.ShowCaption):
            case nameof(TaskbarViewModel.ShowProgress):
            case nameof(TaskbarViewModel.FontSize):
                UpdateLineLayout();
                break;
            case nameof(TaskbarViewModel.IsCalibrating):
                CalibrationFrame.Visibility = _vm.IsCalibrating ? Visibility.Visible : Visibility.Collapsed;
                Cursor = _vm.IsCalibrating ? Cursors.SizeAll : null;
                if (!_vm.IsCalibrating)
                {
                    ForceCursor = false;
                    ReleaseMouseCapture(); // a side drag in progress ends here
                }
                break;
            case "": // settings changed: everything may differ
                ApplyBackground(animate: false);
                UpdateLineLayout();
                break;
        }
    }

    /// <summary>Album gradient pill, cross-faded to the new album's colours.</summary>
    private void ApplyBackground(bool animate)
    {
        Pill.Visibility = _vm.IsGradient ? Visibility.Visible : Visibility.Collapsed;
        if (!_vm.IsGradient)
            return;
        SetStop(GradientStartStop, _vm.GradientStart, animate);
        SetStop(GradientEndStop, _vm.GradientEnd, animate);
    }

    private static void SetStop(GradientStop stop, Color target, bool animate)
    {
        if (animate)
        {
            stop.BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(target, TimeSpan.FromMilliseconds(700)) { EasingFunction = new SineEase() });
        }
        else
        {
            stop.BeginAnimation(GradientStop.ColorProperty, null);
            stop.Color = target;
        }
    }

    /// <summary>
    /// Without the caption the lyric may wrap to two lines, like the weather widget's two rows.
    /// With it, the text is a single line (ellipsis, or a marquee when enabled).
    /// </summary>
    private void UpdateLineLayout()
    {
        // The pill is 40 DIP tall; the progress line takes its bottom edge. Line spacing is
        // fixed (not the font's natural ~1.33×) and capped so the text always fits above the
        // line, even with large fonts and two lyric lines.
        var available = _vm.ShowProgress ? 34.0 : 40.0;
        var lines = _vm.ShowCaption ? 1 : 2;
        var captionHeight = _vm.ShowCaption ? Math.Ceiling(_vm.CaptionFontSize * 1.2) : 0;
        var lineHeight = Math.Min(_vm.FontSize * 1.25, (available - captionHeight) / lines);

        foreach (var tb in new[] { LineA, LineB })
        {
            // WrapWithOverflow never splits a word ("Welcom|e"); a word that does not fit is trimmed.
            tb.TextWrapping = _vm.ShowCaption ? TextWrapping.NoWrap : TextWrapping.WrapWithOverflow;
            tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tb.LineHeight = lineHeight;
            tb.MaxHeight = (lineHeight * lines) + 0.5;
        }
        CaptionText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        CaptionText.LineHeight = Math.Max(1, captionHeight);
        _textLift = _vm.ShowProgress ? 40 - available : 0;
        UpdateTextLift();
        UpdateMarquee();
    }

    /// <summary>
    /// Only text tall enough to reach the progress line (two lines, or a large font) is
    /// lifted; a short single line stays centred with the cover and buttons.
    /// </summary>
    private void UpdateTextLift()
    {
        var lift = TextHost.ActualHeight > 40 - (2 * _textLift) - 4 ? _textLift : 0;
        if (TextHost.Margin.Bottom != lift)
            TextHost.Margin = new Thickness(0, 0, 0, lift);
    }

    private void ShowLine(string text)
    {
        if (_front.Text == text)
            return;

        StopMarquee(_front);
        if (!_vm.EnableAnimations || !IsVisible)
        {
            _front.BeginAnimation(OpacityProperty, null);
            _back.BeginAnimation(OpacityProperty, null);
            _front.Text = text;
            _front.Opacity = 1;
            _back.Opacity = 0;
            UpdateMarquee();
            return;
        }

        // New line slides up from below while the old one fades upwards.
        (_front, _back) = (_back, _front);
        StopMarquee(_front);
        _front.Text = text;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var offset = Math.Max(6, _vm.FontSize * 0.6);

        _front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        ((TranslateTransform)_front.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });

        _back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)_back.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -offset, duration) { EasingFunction = ease });

        Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Scroll a single line that does not fit: pause, scroll to its end, pause, jump back.
    /// Only while visible; animations make WPF render every frame, so this is opt-in.
    /// </summary>
    private void UpdateMarquee()
    {
        var tb = _front;
        var enabled = _vm.Marquee && _vm.ShowCaption && IsVisible && tb.TextWrapping == TextWrapping.NoWrap;
        if (!enabled)
        {
            StopMarquee(tb);
            return;
        }

        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = LineHost.ActualWidth;
        var overflow = tb.DesiredSize.Width - available;
        if (overflow <= 1 || available <= 0)
        {
            StopMarquee(tb);
            return;
        }

        tb.TextTrimming = TextTrimming.None;
        LineHost.Unbounded = true;
        var distance = overflow + 12;
        var scroll = TimeSpan.FromSeconds(distance / MarqueeSpeed);
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(MarqueePause)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(MarqueePause + scroll)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(MarqueePause + scroll + MarqueePause)));
        ((TranslateTransform)tb.RenderTransform).BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void StopMarquee(TextBlock tb)
    {
        LineHost.Unbounded = false;
        ((TranslateTransform)tb.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        ((TranslateTransform)tb.RenderTransform).X = 0;
        tb.TextTrimming = TextTrimming.CharacterEllipsis;
    }
}
