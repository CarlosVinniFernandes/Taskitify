using Taskitify.Platform.Linux;

namespace Taskitify.Linux;

/// <summary>An exclusive lock file: held while the process lives, released by the kernel if it dies.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;

    private SingleInstance(FileStream file) => _lock = file;

    public static SingleInstance? TryAcquire(Xdg xdg)
    {
        var dir = xdg.RuntimeDir is { } r ? Path.Combine(r, Xdg.AppName) : Path.Combine(Path.GetTempPath(), $"{Xdg.AppName}-{Environment.UserName}");
        Directory.CreateDirectory(dir);
        try
        {
            return new SingleInstance(new FileStream(Path.Combine(dir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();
}
