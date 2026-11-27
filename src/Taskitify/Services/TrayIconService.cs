using System.Drawing;
using Forms = System.Windows.Forms;

namespace Taskitify.Services;

/// <summary>Optional notification-area icon (WinForms NotifyIcon, part of the Windows Desktop runtime).</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _debugItem;

    public TrayIconService(Action showSettings, Action showMiniPlayer, Action showLyricCard, Action showDebug, Action exit, Action? showTour = null)
    {
        var menu = new Forms.ContextMenuStrip();

        var playerItem = new Forms.ToolStripMenuItem("", null, (_, _) => showMiniPlayer());
        // Minimal Mode, and the way back when its lyrics let clicks through (they cannot be clicked then).
        var minimalItem = new Forms.ToolStripMenuItem("", null, (_, _) => ToggleMinimal?.Invoke());
        var clicksItem = new Forms.ToolStripMenuItem("", null, (_, _) => ToggleMinimalClicks?.Invoke());
        var cardItem = new Forms.ToolStripMenuItem("", null, (_, _) => showLyricCard());
        var settingsItem = new Forms.ToolStripMenuItem("", null, (_, _) => showSettings());
        var tourItem = showTour is null ? null : new Forms.ToolStripMenuItem("", null, (_, _) => showTour());
        _debugItem = new Forms.ToolStripMenuItem("", null, (_, _) => showDebug());
        var exitItem = new Forms.ToolStripMenuItem("", null, (_, _) => exit());
        menu.Items.Add(playerItem);
        menu.Items.Add(minimalItem);
        menu.Items.Add(clicksItem);
        menu.Items.Add(cardItem);
        menu.Items.Add(settingsItem);
        if (tourItem is not null)
            menu.Items.Add(tourItem);
        menu.Items.Add(_debugItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            playerItem.Text = LocalizationService.Get("ShowPlayer");
            var (minimal, clicksThrough) = Minimal?.Invoke() ?? (false, false);
            minimalItem.Text = LocalizationService.Get("NavMinimal");
            minimalItem.Checked = minimal;
            clicksItem.Text = LocalizationService.Get("MinimalClickThrough");
            clicksItem.Checked = clicksThrough;
            clicksItem.Visible = minimal;
            cardItem.Text = LocalizationService.Get("LyricCardOpen");
            cardItem.Enabled = CanMakeLyricCard?.Invoke() ?? true;
            settingsItem.Text = LocalizationService.Get("Settings");
            if (tourItem is not null)
                tourItem.Text = LocalizationService.Get("TourOpen");
            _debugItem.Text = LocalizationService.Get("Diagnostics");
            exitItem.Text = LocalizationService.Get("Exit");
        };

        _icon = new Forms.NotifyIcon
        {
            Text = "Taskitify",
            Icon = LoadLogo(),
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                showSettings();
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;
    }

    /// <summary>Minimal Mode's state: on, and letting clicks through.</summary>
    public Func<(bool On, bool ClickThrough)>? Minimal { get; set; }

    public Action? ToggleMinimal { get; set; }

    public Action? ToggleMinimalClicks { get; set; }

    /// <summary>Whether a song is playing, so the lyric card item can be used.</summary>
    public Func<bool>? CanMakeLyricCard { get; set; }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    public bool DebugEnabled
    {
        set => _debugItem.Visible = value;
    }

    private Action? _balloonClick;

    /// <param name="onClick">Runs if the user clicks this notification (not a later one).</param>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(onClick is null ? 4000 : 10000, title, text, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

    /// <summary>The app logo at the notification area's icon size (DPI aware).</summary>
    private static Icon LoadLogo()
    {
        using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream("Taskitify.ico")
            ?? throw new InvalidOperationException("Embedded icon Taskitify.ico is missing.");
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
