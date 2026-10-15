using System.Drawing;
using Forms = System.Windows.Forms;

namespace Taskitify.Services;

/// <summary>Optional notification-area icon (WinForms NotifyIcon, part of the Windows Desktop runtime).</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _debugItem;

    public TrayIconService(Action showSettings, Action showMiniPlayer, Action showDebug, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.Get("ShowPlayer"), null, (_, _) => showMiniPlayer());
        menu.Items.Add(LocalizationService.Get("Settings"), null, (_, _) => showSettings());
        _debugItem = new Forms.ToolStripMenuItem(LocalizationService.Get("Diagnostics"), null, (_, _) => showDebug());
        menu.Items.Add(_debugItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("Exit"), null, (_, _) => exit());

        menu.Opening += (_, _) =>
        {
            menu.Items[0].Text = LocalizationService.Get("ShowPlayer");
            menu.Items[1].Text = LocalizationService.Get("Settings");
            _debugItem.Text = LocalizationService.Get("Diagnostics");
            menu.Items[4].Text = LocalizationService.Get("Exit");
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
