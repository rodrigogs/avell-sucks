using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace AvellSucks.UI.Startup;

/// <summary>
/// Manages the "start with Windows" preference via the per-user Run key.
/// Per-user (HKCU) avoids needing admin just to toggle auto-start.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AvellSucks";

    private static string? ExePath
    {
        get
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                // Guard against the dotnet host path during `dotnet run`.
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch { return null; }
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;

            if (enabled)
            {
                var exe = ExePath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry may be locked down; the caller re-reads IsEnabled to reflect reality.
        }
    }
}
