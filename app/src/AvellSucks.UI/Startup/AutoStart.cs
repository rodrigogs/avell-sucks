using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace AvellSucks.UI.Startup;

/// <summary>
/// Manages the "start with Windows" preference.
///
/// The app manifest requests <c>requireAdministrator</c>, so it always launches
/// elevated. That makes the classic HKCU <c>…\Run</c> key unusable for autostart:
/// Windows silently refuses to auto-launch an elevated app from Run at logon (it
/// would need a UAC prompt no one is there to answer). The supported way to start
/// an elevated app at logon WITHOUT a prompt is a Scheduled Task with "run with
/// highest privileges"; the task runs under the logged-on user's full (elevated)
/// token, interactively, silently.
///
/// So enabling autostart creates a per-user logon task (highest privileges);
/// disabling removes it. We also clean up any stale Run-key value a previous
/// build may have written, so the two mechanisms never fight.
/// </summary>
public static class AutoStart
{
    // Task Scheduler path. Leading backslash = root task folder.
    private const string TaskName = "AvellSucks Autostart";
    private const string TaskPath = "\\" + TaskName;

    // Legacy HKCU Run value written by earlier builds — cleaned up on any Set().
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValue = "AvellSucks";

    private static string? ExePath
    {
        get
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch { return null; }
        }
    }

    /// <summary>True when the logon autostart task exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            // /Query exits 0 when the task exists, 1 when it doesn't.
            return RunSchTasks($"/Query /TN \"{TaskName}\"") == 0;
        }
        catch { return false; }
    }

    /// <summary>Create (enable) or delete (disable) the elevated logon task.</summary>
    public static void Set(bool enabled)
    {
        // Whatever we do, drop any legacy Run-key entry so it can't double-launch.
        RemoveLegacyRunValue();

        try
        {
            if (enabled)
            {
                var exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return; // dotnet-host path during `dotnet run` — skip

                // /RL HIGHEST → run elevated with no UAC prompt.
                // /SC ONLOGON → trigger at the current user's logon (interactive token).
                // /F           → overwrite if it already exists (idempotent).
                RunSchTasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
            }
            else
            {
                RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
            }
        }
        catch
        {
            // schtasks missing / access denied — caller re-reads IsEnabled to reflect reality.
        }
    }

    private static void RemoveLegacyRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyRunValue) is not null)
                key.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
        }
        catch { /* best-effort cleanup */ }
    }

    // Run schtasks.exe hidden and return its exit code (-1 if it couldn't start).
    private static int RunSchTasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.WaitForExit();
        return p.ExitCode;
    }
}
