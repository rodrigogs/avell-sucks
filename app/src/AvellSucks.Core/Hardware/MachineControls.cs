namespace AvellSucks.Core.Hardware;

/// <summary>Truthful outcome for a machine-control operation.</summary>
public enum MachineControlOutcome
{
    Blocked,
    Failed,
    Requested,
    Verified,
}

/// <summary>
/// Result shared by the UI, REST API and MCP surface. Requested means the OS
/// accepted a best-effort command whose physical effect cannot be queried (for
/// example SC_MONITORPOWER); it is deliberately not reported as verified.
/// </summary>
public sealed record MachineControlResult(
    DateTimeOffset Timestamp,
    string Control,
    string Action,
    MachineControlOutcome Outcome,
    string? Message = null,
    string? Before = null,
    string? After = null,
    string? Reason = null,
    string? Origin = null,
    string? Identity = null)
{
    public bool Allowed => Outcome is not MachineControlOutcome.Blocked;
    public bool Executed => Outcome is MachineControlOutcome.Requested or MachineControlOutcome.Verified;
    public bool Verified => Outcome is MachineControlOutcome.Verified;
}

/// <summary>Current control state as observed from the EC and Windows.</summary>
public sealed record MachineControlStatus(
    DateTimeOffset Timestamp,
    bool SupportedMachine,
    bool? WirelessRadiosEnabled,
    bool WifiPresent,
    bool BluetoothPresent,
    bool? TouchpadEnabled,
    bool? WebcamEnabled,
    byte? BrightnessPercent,
    bool DisplayPowerControlAvailable,
    string? Error);

/// <summary>Windows-owned state that does not live in the EC.</summary>
public sealed record PlatformMachineControlStatus(
    bool SupportedMachine,
    bool WifiPresent,
    bool BluetoothPresent,
    bool? TouchpadEnabled,
    bool? WebcamEnabled,
    byte? BrightnessPercent,
    bool DisplayPowerControlAvailable,
    string? Error);

/// <summary>Result returned by the Windows-specific backend to the portable orchestrator.</summary>
public sealed record PlatformMutationResult(bool Executed, bool? Verified, string? Message)
{
    public static PlatformMutationResult Success(string? message = null) => new(true, true, message);
    public static PlatformMutationResult Accepted(string? message = null) => new(true, null, message);
    public static PlatformMutationResult Failure(string message) => new(false, false, message);
}

/// <summary>
/// Windows/platform seam for controls that are not raw EC transactions. The
/// portable MachineControlService owns the write gate, rollback and audit.
/// </summary>
public interface IPlatformMachineControlBackend
{
    ValueTask<PlatformMachineControlStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    ValueTask<PlatformMutationResult> ReconcileWirelessRadiosAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask<PlatformMutationResult> SetTouchpadEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask<PlatformMutationResult> SetWebcamEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask<PlatformMutationResult> SetBrightnessAsync(byte percent, CancellationToken cancellationToken = default);
    ValueTask<PlatformMutationResult> TurnOffDisplayAsync(CancellationToken cancellationToken = default);
}

public interface IMachineControlService
{
    ValueTask<MachineControlStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    ValueTask<MachineControlResult> SetWirelessRadiosAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null);

    ValueTask<MachineControlResult> SetTouchpadEnabledAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null);

    ValueTask<MachineControlResult> SetWebcamEnabledAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null);

    ValueTask<MachineControlResult> SetBrightnessAsync(
        int percent, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null);

    ValueTask<MachineControlResult> TurnOffDisplayAsync(
        string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null);
}

public interface IMachineControlAuditLog
{
    ValueTask RecordAsync(MachineControlResult result, CancellationToken cancellationToken = default);
}
