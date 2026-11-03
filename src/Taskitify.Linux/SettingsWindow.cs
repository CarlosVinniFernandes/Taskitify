using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Taskitify.Linux;

internal sealed class SettingsWindow : Window
{
    private readonly AppHost _host;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        var s = host.Settings;
        Title = host.T("SettingsTitle").Replace("Windows", "").Trim();
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };

        var language = new ComboBox { ItemsSource = host.Languages.Select(l => l.Name).ToList(), MinWidth = 180 };
        language.SelectedIndex = Math.Max(0, host.Languages.ToList().FindIndex(l => l.Code == host.Language));
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex >= 0)
            {
                s.Language = host.Languages[language.SelectedIndex].Code;
                host.SaveSettings();
            }
        };
        panel.Children.Add(Labelled(host.T("Language"), language));

        panel.Children.Add(Labelled(host.T("FontSize"), Number(s.FontSize, 10, 48, 1, v => { s.FontSize = (int)v; host.SaveSettings(); })));
        panel.Children.Add(Labelled(host.L("Width"), Number(s.Width, 200, 1600, 20, v => { s.Width = (int)v; host.SaveSettings(); })));

        var offset = Number(s.SyncOffsetMs, -10_000, 10_000, 50, v => { s.SyncOffsetMs = (int)v; host.SaveSettings(); });
        panel.Children.Add(Labelled(host.T("Offset") + " (ms)", offset));
        panel.Children.Add(new TextBlock { Text = host.T("OffsetHint"), Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });

        panel.Children.Add(Check(host.L("FloatingBar"), host.FloatingBar, v => { s.FloatingBar = v; host.SaveSettings(); }));
        panel.Children.Add(Check(host.L("HideWhenPaused"), s.HideWhenPaused, v => { s.HideWhenPaused = v; host.SaveSettings(); }));
        panel.Children.Add(Check(host.L("ShowTrackWhenNoLyrics"), s.ShowTrackWhenNoLyrics, v => { s.ShowTrackWhenNoLyrics = v; host.SaveSettings(); }));
        panel.Children.Add(Check(host.L("StartAtLogin"), host.AutostartEnabled, v =>
        {
            s.StartAtLogin = v;
            host.ApplyAutostart();
            host.SaveSettings();
        }));

        var folder = new Button { Content = host.T("OpenLyricsFolder"), HorizontalAlignment = HorizontalAlignment.Left };
        folder.Click += (_, _) => host.OpenLyricsFolder();
        panel.Children.Add(folder);

        Content = panel;
    }

    private static Control Labelled(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        grid.Children.Add(input);
        Grid.SetColumn(input, 1);
        return grid;
    }

    private static NumericUpDown Number(double value, double min, double max, double step, Action<double> changed)
    {
        var box = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, Value = (decimal)value, FormatString = "0", MinWidth = 140 };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v)
                changed((double)v);
        };
        return box;
    }

    private static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }
}
