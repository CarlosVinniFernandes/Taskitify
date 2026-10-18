using System.IO;

namespace Taskitify.Infrastructure;

/// <summary>Tiny logger: in-memory ring buffer always, file only in debug mode.</summary>
internal static class Log
{
    private const int Capacity = 400;
    private const long MaxFileBytes = 1024 * 1024;
    private static readonly Queue<string> Lines = new();
    private static readonly object Gate = new();

    public static bool WriteToFile { get; set; }

    public static string FilePath { get; } = Path.Combine(AppPaths.LocalDataDirectory, "log.txt");

    public static event Action<string>? LineAdded;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate)
            return Lines.ToList();
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {message}";
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();
            if (WriteToFile)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LocalDataDirectory);
                    // Keep at most ~2 MB on disk: current file plus one previous.
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxFileBytes)
                        File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
                catch (IOException)
                {
                }
            }
        }
        LineAdded?.Invoke(line);
    }
}

internal static class AppPaths
{
    /// <summary>%AppData%\Taskitify — settings.</summary>
    public static string RoamingDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Taskitify");

    /// <summary>%LocalAppData%\Taskitify — cache and logs.</summary>
    public static string LocalDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskitify");

    public static string LyricsCacheDirectory { get; } = Path.Combine(LocalDataDirectory, "lyrics-cache");

    /// <summary>%AppData%\Taskitify\lyrics — the user's own .lrc files, checked before LRCLIB.</summary>
    public static string UserLyricsDirectory { get; } = Path.Combine(RoamingDirectory, "lyrics");

    /// <summary>
    /// The app was called "Taskbar Lyrics" before 1.0. Move its settings and lyrics cache to
    /// the new folders once, so existing users keep their setup.
    /// </summary>
    public static void MigrateFromOldName()
    {
        const string oldName = "TaskbarLyrics";
        foreach (var (oldDir, newDir) in new[]
        {
            (Path.Combine(Path.GetDirectoryName(RoamingDirectory)!, oldName), RoamingDirectory),
            (Path.Combine(Path.GetDirectoryName(LocalDataDirectory)!, oldName), LocalDataDirectory),
        })
        {
            try
            {
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                    Directory.Move(oldDir, newDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not fatal: the app simply starts with default settings.
            }
        }
    }
}
