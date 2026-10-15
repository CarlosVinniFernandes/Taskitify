using Microsoft.Win32;
using Taskitify.Infrastructure;

namespace Taskitify.Services;

/// <summary>"Start with Windows" through HKCU\...\Run (per user, no admin rights).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Taskitify";
    private const string OldValueName = "TaskbarLyrics"; // name before 1.0
    public const string BackgroundArgument = "--background";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Carry a "start with Windows" entry from the old app name over to the new one.</summary>
    public static void MigrateFromOldName()
    {
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
