using Microsoft.Win32;

namespace iFlyCompassGUI.Helpers;

public static class RegistryHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "iFlyCompassGUI";
    
    public static void SetAutoStart(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        
        if (enabled)
            key.SetValue(AppName, executablePath);
        else
            key.DeleteValue(AppName, false);
    }
    
    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }
}
