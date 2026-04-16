using Microsoft.Win32;

namespace LabelPrintClient.Services;

public static class StartupManager
{
    private const string AppName = "LabelPrintClient";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) != null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? "";
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
