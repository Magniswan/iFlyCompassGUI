using Microsoft.Win32;
using Windows.ApplicationModel;

namespace iFlyCompassGUI.Helpers;

public static class RegistryHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "iFlyCompassGUI";
    private const string StartupTaskId = "iFlyCompassGUIStartup";

    public static void SetAutoStart(bool enabled)
    {
        if (PathHelper.IsPackaged)
        {
            SetAutoStartPackaged(enabled);
        }
        else
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            SetAutoStartUnpackaged(enabled, exePath);
        }
    }

    public static bool IsAutoStartEnabled()
    {
        if (PathHelper.IsPackaged)
        {
            return IsAutoStartEnabledPackaged();
        }

        return IsAutoStartEnabledUnpackaged();
    }

    private static void SetAutoStartPackaged(bool enabled)
    {
        try
        {
            var task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();
            if (enabled)
            {
                if (task.State == StartupTaskState.Disabled)
                {
                    task.RequestEnableAsync().GetAwaiter().GetResult();
                }
            }
            else
            {
                if (task.State == StartupTaskState.Enabled)
                {
                    task.Disable();
                }
            }
        }
        catch
        {
            // StartupTask may not be configured in the manifest
        }
    }

    private static bool IsAutoStartEnabledPackaged()
    {
        try
        {
            var task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();
            return task.State == StartupTaskState.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutoStartUnpackaged(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (enabled)
            key.SetValue(AppName, executablePath);
        else
            key.DeleteValue(AppName, false);
    }

    private static bool IsAutoStartEnabledUnpackaged()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }
}
