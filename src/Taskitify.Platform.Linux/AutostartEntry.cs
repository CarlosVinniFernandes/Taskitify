using System.Text;

namespace Taskitify.Platform.Linux;

/// <summary>Start-at-login through an XDG autostart .desktop file.</summary>
public sealed class AutostartEntry
{
    private readonly string _file;

    public AutostartEntry(string file) => _file = file;

    public bool IsEnabled => File.Exists(_file);

    public void Set(bool enabled, string execPath)
    {
        if (!enabled)
        {
            if (File.Exists(_file))
                File.Delete(_file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, Build(execPath));
    }

    public static string Build(string execPath)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("Type=Application\n");
        sb.Append("Name=Taskitify\n");
        sb.Append("Comment=Synced Spotify lyrics\n");
        sb.Append("Exec=").Append(EscapeExec(execPath)).Append(" --background\n");
        sb.Append("Icon=taskitify\n");
        sb.Append("Terminal=false\n");
        sb.Append("Categories=AudioVideo;Audio;Player;\n");
        sb.Append("X-GNOME-Autostart-enabled=true\n");
        return sb.ToString();
    }

    /// <summary>Desktop Entry spec: quote an Exec program with spaces or reserved characters.</summary>
    internal static string EscapeExec(string path)
    {
        const string reserved = " \t\n\"'\\><~|&;$*?#()`";
        if (path.IndexOfAny(reserved.ToCharArray()) < 0)
            return path;
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return "\"" + escaped + "\"";
    }
}
