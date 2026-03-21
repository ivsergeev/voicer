using Microsoft.Win32;
using Serilog;

namespace OpenVoicer.Services;

/// <summary>
/// Manages Windows autostart via Registry (HKCU\...\Run).
/// </summary>
public static class AutoStartService
{
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OpenVoicer";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AutoStart] Error");
        }
    }
}
