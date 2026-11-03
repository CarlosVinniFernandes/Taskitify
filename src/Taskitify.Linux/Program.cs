using Avalonia;
using Taskitify.Platform.Linux;

namespace Taskitify.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"taskitify {typeof(Program).Assembly.GetName().Version?.ToString(3)}");
            return 0;
        }

        // One instance per login session: a second launch (autostart + app menu) just exits.
        using var instance = SingleInstance.TryAcquire(new Xdg());
        if (instance is null)
        {
            Console.Error.WriteLine("Taskitify is already running.");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
