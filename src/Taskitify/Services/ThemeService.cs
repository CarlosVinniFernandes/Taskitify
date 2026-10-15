using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>
/// Follows the Windows light/dark setting for apps (mini player, settings). The overlay
/// instead derives its colours from the sampled taskbar pixels.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public ThemeService()
    {
        Refresh();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool AppsUseLightTheme { get; private set; }

    public bool TaskbarUsesLightTheme { get; private set; }

    public event EventHandler? Changed;

    public void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        AppsUseLightTheme = key?.GetValue("AppsUseLightTheme") is int a && a != 0;
        TaskbarUsesLightTheme = key?.GetValue("SystemUsesLightTheme") is int s && s != 0;
        Apply();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color))
            return;
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var before = (AppsUseLightTheme, TaskbarUsesLightTheme);
            Refresh();
            if (before != (AppsUseLightTheme, TaskbarUsesLightTheme))
            {
                Log.Info($"Theme changed: apps light={AppsUseLightTheme}, taskbar light={TaskbarUsesLightTheme}");
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void Apply()
    {
        var res = Application.Current.Resources;
        var light = AppsUseLightTheme;
        Set(res, "PopupBackgroundBrush", light ? Color.FromArgb(0xF7, 0xF3, 0xF3, 0xF3) : Color.FromArgb(0xF7, 0x20, 0x20, 0x20));
        Set(res, "PopupBorderBrush", light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Set(res, "TextPrimaryBrush", light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        Set(res, "TextSecondaryBrush", light ? Color.FromArgb(0x9E, 0, 0, 0) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        Set(res, "TextTertiaryBrush", light ? Color.FromArgb(0x5C, 0, 0, 0) : Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));
        Set(res, "SubtleFillBrush", light ? Color.FromArgb(0x0A, 0, 0, 0) : Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        Set(res, "SubtleFillHoverBrush", light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        Set(res, "TrackBrush", light ? Color.FromArgb(0x33, 0, 0, 0) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        Set(res, "WindowBackgroundBrush", light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20));
        Set(res, "ControlBackgroundBrush", light ? Color.FromRgb(0xFB, 0xFB, 0xFB) : Color.FromRgb(0x2B, 0x2B, 0x2B));
        Set(res, "AccentBrush", SystemParameters.WindowGlassColor.A > 0 ? Brighten(SystemParameters.WindowGlassColor, light) : Color.FromRgb(0x1D, 0xB9, 0x54));
    }

    private static Color Brighten(Color c, bool light)
    {
        // The DWM colour can be very dark; keep the accent readable on the popup background.
        var lum = ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;
        if (!light && lum < 0.45)
        {
            var f = 0.45 / Math.Max(lum, 0.05);
            return Color.FromRgb((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));
        }
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static void Set(ResourceDictionary res, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        res[key] = brush;
    }
}
