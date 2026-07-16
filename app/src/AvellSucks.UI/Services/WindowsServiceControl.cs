using System;
using System.Diagnostics;

namespace AvellSucks.UI.Services;

/// <summary>
/// Installs/controls the AvellSucks control service via sc.exe. The UI already
/// runs elevated, so these succeed without an extra prompt. Best-effort: callers
/// re-query IsInstalled/IsRunning to reflect reality.
/// </summary>
public sealed class WindowsServiceControl
{
    public const string ServiceName = "AvellSucksControl";
    public const string DisplayName = "AvellSucks Control Service";

    public bool IsInstalled() => Sc($"query \"{ServiceName}\"") == 0;

    public bool IsRunning()
    {
        var (code, output) = ScWithOutput($"query \"{ServiceName}\"");
        return code == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    public void Install(string exePath)
    {
        // binPath must point at the server exe; auto-start disabled (UI starts it).
        Sc($"create \"{ServiceName}\" binPath= \"{exePath}\" DisplayName= \"{DisplayName}\" start= demand");
    }

    public void Uninstall()
    {
        Stop();
        Sc($"delete \"{ServiceName}\"");
    }

    public void Start() => Sc($"start \"{ServiceName}\"");
    public void Stop() => Sc($"stop \"{ServiceName}\"");

    private static int Sc(string args) => ScWithOutput(args).code;

    private static (int code, string output) ScWithOutput(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
