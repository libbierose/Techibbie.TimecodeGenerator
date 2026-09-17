using System;
using Microsoft.Win32;

namespace Techibbie.TimecodeGenerator.App.Services;

/// <summary>
/// Toggles "run at login" via the current user's Run registry key — no installer/MSIX
/// required, works for a plain self-contained exe. Windows-only for now; <see cref="IsSupported"/>
/// gates the Settings checkbox off on macOS/Linux rather than pretending to support it there.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TechibbieTimecodeGenerator";

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(ValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort — a failure here shouldn't crash Settings.
        }
    }
}
