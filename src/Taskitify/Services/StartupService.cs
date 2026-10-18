using Microsoft.Win32;
using Taskitify.Infrastructure;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Taskitify.Services;

/// <summary>MSIX startup task, or HKCU Run for the portable build.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Taskitify";
    private const string OldValueName = "TaskbarLyrics"; // name before 1.0
    public const string BackgroundArgument = "--background";
    public const string TaskId = "TaskitifyStartup";
    private static StartupTask? _startupTask;
    public static bool IsPackaged { get; } = HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        try { _ = Package.Current.Id; return true; }
        catch (InvalidOperationException) { return false; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    public static bool IsStartupActivation()
    {
        if (!IsPackaged) return false;
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch (Exception ex) { Log.Error("Could not read package activation", ex); return false; }
    }

    public static async Task InitializeAsync()
    {
        if (!IsPackaged) return;
        try { _startupTask = await StartupTask.GetAsync(TaskId); }
        catch (Exception ex) { Log.Error("Could not read packaged startup task", ex); }
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        if (!IsPackaged) { SetEnabled(enabled); return; }
        try
        {
            _startupTask ??= await StartupTask.GetAsync(TaskId);
            if (enabled)
                await _startupTask.RequestEnableAsync();
            else
                _startupTask.Disable();
        }
        catch (Exception ex) { Log.Error("Could not change packaged startup task", ex); }
    }

    public static bool IsEnabled
    {
        get
        {
            if (IsPackaged)
                return _startupTask?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Carry a "start with Windows" entry from the old app name over to the new one.</summary>
    public static void MigrateFromOldName()
    {
        if (IsPackaged) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(OldValueName) is not string)
                return;
            key.DeleteValue(OldValueName, throwOnMissingValue: false);
            SetEnabled(true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not migrate startup registration", ex);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");
                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not change startup registration", ex);
        }
    }
}
