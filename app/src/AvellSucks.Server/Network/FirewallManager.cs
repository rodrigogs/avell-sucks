using System.Diagnostics;

namespace AvellSucks.Server.Network;

/// <summary>Runs an external command and returns its exit code + captured output.</summary>
public interface ICommandRunner
{
    int Run(string file, string args, out string output);
}

/// <summary>Real runner: launches a process hidden and waits for it.</summary>
public sealed class NetshCommandRunner : ICommandRunner
{
    public int Run(string file, string args, out string output)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) { output = "failed to start"; return -1; }
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}

/// <summary>
/// Opens/closes the inbound Windows Firewall rule for the control-service port.
/// Only used when the operator turns on FirewallAutoOpen; default is closed
/// (no rule created). Requires elevation (the service runs elevated).
/// </summary>
public sealed class FirewallManager(ICommandRunner runner)
{
    public const string RuleName = "AvellSucks Control Service";

    /// <summary>Delete any stale rule, then add an inbound TCP allow for the port.</summary>
    public void OpenPort(int port)
    {
        runner.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"", out _);
        runner.Run("netsh",
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}",
            out _);
    }

    /// <summary>Remove the rule (idempotent — no error if absent).</summary>
    public void ClosePort(int port)
        => runner.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"", out _);
}
