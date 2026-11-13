using System.Text.Json;

namespace Taskitify.Platform.Linux;

/// <summary>
/// Publishes the current lyric line to a small JSON file so other processes (the GNOME Shell
/// extension in <c>gnome-extension/</c>, a Waybar module...) can show it. Written atomically.
/// </summary>
public sealed class LineFile
{
    private readonly string? _path;

    public LineFile(string? path) => _path = path;

    public void Write(string? text, string? title, string? artist, bool playing)
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, Serialize(text, title, artist, playing));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Best effort: nobody may be listening.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        if (_path is null)
            return;
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    public static string Serialize(string? text, string? title, string? artist, bool playing) =>
        JsonSerializer.Serialize(new { line = text ?? "", title = title ?? "", artist = artist ?? "", playing });
}
