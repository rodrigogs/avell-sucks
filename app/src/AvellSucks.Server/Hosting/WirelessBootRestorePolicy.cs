using AvellSucks.Core.Hardware;

namespace AvellSucks.Server.Hosting;

/// <summary>
/// Pure decision for the Session 0 wireless boot-restore. No I/O: it reads the
/// observed machine/radio state and the persisted config, and returns whether the
/// service should re-enable the radios, plus a human-readable reason. Kept
/// separate from <see cref="WirelessBootRestoreService"/> so the branching is
/// unit-testable without a live EC/PnP tree.
/// </summary>
public static class WirelessBootRestorePolicy
{
    /// <summary>
    /// Decide whether to call <c>SetWirelessRadiosAsync(true)</c> at service
    /// startup. One-directional: this NEVER authorizes turning radios off.
    /// </summary>
    /// <param name="configFlag">The persisted user intent (<c>RestoreWirelessRadiosOnBoot</c>).</param>
    /// <param name="restoreEnabled">The dedicated env gate (<c>GAMINGCENTER_RESTORE_RADIOS</c>). False disables even with the flag on.</param>
    /// <param name="status">The observed machine-control status (may be null if the hardware probe failed).</param>
    public static RestoreDecision Decide(bool configFlag, bool restoreEnabled, MachineControlStatus? status)
    {
        if (!restoreEnabled)
            return new(false, "Restore disabled by env (GAMINGCENTER_RESTORE_RADIOS!=1).");

        if (!configFlag)
            return new(false, "Restore not requested (RestoreWirelessRadiosOnBoot is false).");

        if (status is null)
            return new(false, "Machine-control status unavailable; leaving radios untouched.");

        if (!status.SupportedMachine)
            return new(false, "Unsupported machine model; radio restore is a no-op.");

        // Only restore when we actually KNOW the radio state. An unknown
        // (null) state means the EC read failed — touching the radio blind
        // could fight a watchdog or a hardware kill switch. Bail out safely.
        if (!status.WirelessRadiosEnabled.HasValue)
            return new(false, "Radio state is unknown (EC read incomplete); not touching the radios.");

        if (status.WirelessRadiosEnabled.Value)
            return new(false, "Radios are already ON; nothing to restore.");

        return new(true, "Radios are OFF and the user previously had them ON — restoring.");
    }

    /// <param name="ShouldRestore">True only when the service must re-enable the radios.</param>
    /// <param name="Reason">Human-readable trace reason (always set, even on a no-op).</param>
    public readonly record struct RestoreDecision(bool ShouldRestore, string Reason);
}
