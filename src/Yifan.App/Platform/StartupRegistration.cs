using Microsoft.Win32;

namespace Yifan.Platform;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled)
            key.SetValue(Yifan.Core.Desktop.ShellPolicy.RunValueName, $"\"{exePath}\" {Yifan.Core.Desktop.ShellPolicy.TrayArgument}");
        else
            key.DeleteValue(Yifan.Core.Desktop.ShellPolicy.RunValueName, throwOnMissingValue: false);
    }
}
