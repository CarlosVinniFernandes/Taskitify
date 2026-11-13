using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Taskitify.Linux;

/// <summary>
/// The lyrics bar: a borderless, always-on-top, non-focus-stealing window with the current line on the
/// cover's gradient. Drag it anywhere (the spot is remembered); click it to open the mini player.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const double DragThreshold = 4;

    private readonly AppHost _host;
    private readonly Border _frame;
    private readonly Grid _row;
    private readonly TextBlock _text;
    private readonly StackPanel _buttons;
    private Control? _cover;
    private PixelPoint? _dragStartWindow;
    private PixelPoint _dragStartPointer;
    private bool _moved;
    private bool _positioned;

    public OverlayWindow(AppHost host)
    {
        _host = host;
        Title = "Taskitify";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Focusable = false;

        _text = new TextBlock
        {
            Foreground = Ui.White,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            Margin = new Thickness(10, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _row.Children.Add(_text);
        Grid.SetColumn(_text, 1);
        _row.Children.Add(_buttons);
        Grid.SetColumn(_buttons, 2);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 8),
            MinHeight = 52,
            Child = _row,
        };
        Content = _frame;
        ContextMenu = BuildMenu();

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        _host.Changed += Refresh;
        Refresh();
    }

    public event Action? MiniPlayerRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) => menu.ItemsSource = MenuItems();
        return menu;
    }

    private List<Control> MenuItems()
    {
        var items = new List<Control>();
        items.Add(Item(_host.T("ShowPlayer"), () => MiniPlayerRequested?.Invoke()));

        var reload = new MenuItem { Header = _host.T("ReloadLyrics") };
        reload.Click += (_, _) => _ = _host.Session.ForceReloadAsync();
        foreach (var source in _host.Session.Sources)
        {
            var name = source;
            reload.Items.Add(Item(_host.Tf("ReloadLyricsFrom", name), () => _ = _host.Session.ReloadFromAsync(name)));
        }
        items.Add(reload);

        items.Add(Item(_host.L("ResetPosition"), ResetPosition));
        items.Add(Item(_host.T("Settings"), () => SettingsRequested?.Invoke()));
        items.Add(new Separator());
        items.Add(Item(_host.T("Exit"), () => ExitRequested?.Invoke()));
        return items;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void Refresh()
    {
        var text = _host.BarText();
        var visible = _host.FloatingBar && _host.PlayerVisible && text.Length > 0;
        if (!visible)
        {
            if (IsVisible)
                Hide();
            return;
        }

        _text.Text = text;
        _text.FontSize = _host.Settings.FontSize;
        Width = _host.Settings.Width;
        _frame.Background = Ui.Gradient(_host.Cover);

        var status = _host.Media.Status;
        if (_cover is not null)
            _row.Children.Remove(_cover);
        _cover = _host.Cover is null ? null : Ui.CoverView(_host.Cover, 36, 6);
        if (_cover is not null)
        {
            _row.Children.Add(_cover);
            Grid.SetColumn(_cover, 0);
        }

        _buttons.Children.Clear();
        _buttons.Children.Add(Ui.IconButton(Icons.Previous, _host.T("Previous"), () => _ = _host.Media.PreviousAsync(), 14));
        _buttons.Children.Add(Ui.IconButton(Ui.PlayPathFor(status), _host.T("PlayPause"), () => _ = _host.Media.PlayPauseAsync(), 16));
        _buttons.Children.Add(Ui.IconButton(Icons.Next, _host.T("Next"), () => _ = _host.Media.NextAsync(), 14));

        if (!IsVisible)
        {
            Show();
            Position = ResolveStartPosition();
        }
        else if (!_positioned)
        {
            Position = ResolveStartPosition();
        }
    }

    private PixelPoint ResolveStartPosition()
    {
        _positioned = true;
        if (_host.Settings is { WindowX: { } x, WindowY: { } y })
            return new PixelPoint((int)x, (int)y);
        return DefaultPosition();
    }

    private PixelPoint DefaultPosition()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = Screens.Primary?.Scaling ?? 1;
        var width = (int)(_host.Settings.Width * scale);
        return new PixelPoint(area.X + ((area.Width - width) / 2), area.Y + 8);
    }

    private void ResetPosition()
    {
        _host.Settings.WindowX = null;
        _host.Settings.WindowY = null;
        _host.SaveSettings();
        Position = DefaultPosition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Buttons handle their own clicks.
        if (e.Source is Visual v && v.FindAncestorOfType<Button>() is not null)
            return;
        _dragStartWindow = Position;
        _dragStartPointer = this.PointToScreen(e.GetPosition(this));
        _moved = false;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartWindow is not { } start)
            return;
        var now = this.PointToScreen(e.GetPosition(this));
        var dx = now.X - _dragStartPointer.X;
        var dy = now.Y - _dragStartPointer.Y;
        if (!_moved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
            return;
        _moved = true;
        Position = new PixelPoint(start.X + dx, start.Y + dy);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStartWindow is null)
            return;
        e.Pointer.Capture(null);
        _dragStartWindow = null;
        if (_moved)
        {
            _host.Settings.WindowX = Position.X;
            _host.Settings.WindowY = Position.Y;
            _host.SaveSettings();
        }
        else if (e.InitialPressMouseButton == MouseButton.Left)
        {
            MiniPlayerRequested?.Invoke();
        }
    }
}
