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
        menu.Items.Add(LocalizationService.Get("ShowPlayer"), null, (_, _) => showMiniPlayer());
        var cardItem = new Forms.ToolStripMenuItem(LocalizationService.Get("LyricCardOpen"), null, (_, _) => showLyricCard());
        menu.Items.Add(cardItem);
        menu.Items.Add(LocalizationService.Get("Settings"), null, (_, _) => showSettings());
        // Temporary, for reviewing the welcome tour before it is wired into the first run.
        if (showTour is not null)
            menu.Items.Add(LocalizationService.Get("TourOpen"), null, (_, _) => showTour());
        _debugItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Diagnostics"), null, (_, _) => showDebug());
        menu.Items.Add(_debugItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("Exit"), null, (_, _) => exit());

        menu.Opening += (_, _) =>
        {
            menu.Items[0].Text = LocalizationService.Get("ShowPlayer");
            cardItem.Text = LocalizationService.Get("LyricCardOpen");
            cardItem.Enabled = CanMakeLyricCard?.Invoke() ?? true;
            menu.Items[2].Text = LocalizationService.Get("Settings");
            _debugItem.Text = LocalizationService.Get("Diagnostics");
            menu.Items[5].Text = LocalizationService.Get("Exit");
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
    }

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

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);

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
