using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Taskitify.Platform.Linux;

namespace Taskitify.Linux;

public sealed class App : Application
{
    private AppHost? _host;
    private OverlayWindow? _overlay;
    private MiniPlayerWindow? _mini;
    private SettingsWindow? _settings;
    private TrayIcon? _tray;
    private NativeMenuItem? _showPlayerItem;
    private NativeMenuItem? _settingsItem;
    private NativeMenuItem? _exitItem;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = new AppHost(new Xdg());
            _overlay = new OverlayWindow(_host);
            _overlay.MiniPlayerRequested += ToggleMiniPlayer;
            _overlay.SettingsRequested += ShowSettings;
            _overlay.ExitRequested += () => Exit(desktop);
            BuildTray(desktop);
            desktop.Exit += (_, _) => _host.Dispose();
            _host.Changed += RefreshTray;
            _ = _host.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var host = _host!;
        _showPlayerItem = new NativeMenuItem();
        _showPlayerItem.Click += (_, _) => ToggleMiniPlayer();
        _settingsItem = new NativeMenuItem();
        _settingsItem.Click += (_, _) => ShowSettings();
        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => Exit(desktop);

        using var icon = AssetLoader.Open(new Uri("avares://taskitify/Assets/taskitify.png"));
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(icon),
            ToolTipText = "Taskitify",
            Menu = new NativeMenu { _showPlayerItem, _settingsItem, new NativeMenuItemSeparator(), _exitItem },
        };
        _tray.Clicked += (_, _) => ToggleMiniPlayer();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        RefreshTray();
        _ = host;
    }

    private void RefreshTray()
    {
        if (_host is not { } host || _showPlayerItem is null)
            return;
        _showPlayerItem.Header = host.T("ShowPlayer");
        _settingsItem!.Header = host.T("Settings");
        _exitItem!.Header = host.T("Exit");
        if (_tray is not null)
        {
            _tray.ToolTipText = host.Media.Track is { } t ? $"Taskitify — {t.Title}" : "Taskitify";
        }
    }

    private void ToggleMiniPlayer()
    {
        if (_host is null)
            return;
        if (_mini is { IsVisible: true })
        {
            _mini.Hide();
            return;
        }
        _mini ??= new MiniPlayerWindow(_host);
        _mini.Show();
        _mini.Activate();
    }

    private void ShowSettings()
    {
        if (_host is null)
            return;
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_host);
        _settings.Show();
    }

    private static void Exit(IClassicDesktopStyleApplicationLifetime desktop) => desktop.Shutdown();
}
