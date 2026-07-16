namespace AvellSucks.Core.Service;

/// <summary>Outcome of the remote-write check: allowed, or blocked with a reason.</summary>
public sealed record RemoteWriteDecision(bool Allowed, string? Reason);

/// <summary>
/// The SECOND write gate (the first is <see cref="Platforms.WriteGate"/>): a
/// non-loopback caller may actuate hardware only when the operator has turned on
/// AllowRemoteWrites. Loopback callers are unaffected. Pure decision logic —
/// callers extract the two inputs from the request and config.
/// </summary>
public static class RemoteWriteGate
{
    public static RemoteWriteDecision Evaluate(bool callerIsLoopback, bool allowRemoteWrites)
    {
        if (callerIsLoopback) return new RemoteWriteDecision(true, null);
        if (allowRemoteWrites) return new RemoteWriteDecision(true, null);
        return new RemoteWriteDecision(
            false,
            "Denied: remote hardware writes are disabled. "
            + "Enable them in Settings → Remote access (off by default).");
    }
}
