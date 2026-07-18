using AvellSucks.Core.Platforms;

namespace AvellSucks.Core.Hardware;

/// <summary>
/// Portable orchestration for model-specific EC radio control plus Windows-owned
/// device controls. Every mutation passes the shared hardware-write gate and an
/// operation-level audit. The radio path is intentionally specialized: 0x47B is
/// the state byte and 0x7A1 is an edge-trigger byte consumed by the GJ5CN EC, so
/// the generic exact-readback SafeEcWriter is not appropriate for the trigger.
/// </summary>
public sealed class MachineControlService : IMachineControlService
{
    public const int DeviceStateAddress = 0x47B;
    public const int RadioTriggerAddress = 0x7A1;
    public const int WirelessRadioMask = 0xA0; // WLAN 0x80 + Bluetooth 0x20

    private readonly IEcBackend _reader;
    private readonly IEcWriter _writer;
    private readonly IPlatformMachineControlBackend _platform;
    private readonly WriteGate _gate;
    private readonly IMachineControlAuditLog _audit;

    public MachineControlService(
        IEcBackend reader,
        IEcWriter writer,
        IPlatformMachineControlBackend platform,
        WriteGate gate,
        IMachineControlAuditLog audit)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async ValueTask<MachineControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        PlatformMachineControlStatus platform;
        try
        {
            platform = await _platform.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            platform = new(false, false, false, null, null, null, false,
                $"Platform status failed: {ex.Message}");
        }

        bool? radios = null;
        string? ecError = null;
        try
        {
            if (!platform.SupportedMachine)
            {
                ecError = "Machine controls are supported only on the verified Avell 1555 platform.";
            }
            else
            {
                var snapshot = await _reader.ReadSnapshotAsync([DeviceStateAddress], cancellationToken)
                    .ConfigureAwait(false);
                var field = snapshot.Fields.FirstOrDefault();
                if (field is { Ok: true })
                    radios = (field.Value & WirelessRadioMask) == WirelessRadioMask;
                else
                    ecError = field?.Error ?? "EC device-state read returned no field.";
            }
        }
        catch (Exception ex)
        {
            ecError = $"EC device-state read failed: {ex.Message}";
        }

        var error = JoinErrors(ecError, platform.Error);
        return new MachineControlStatus(
            DateTimeOffset.UtcNow,
            platform.SupportedMachine,
            radios,
            platform.WifiPresent,
            platform.BluetoothPresent,
            platform.TouchpadEnabled,
            platform.WebcamEnabled,
            platform.BrightnessPercent,
            platform.DisplayPowerControlAvailable,
            error);
    }

    public async ValueTask<MachineControlResult> SetWirelessRadiosAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
    {
        const string control = "wireless-radios";
        var action = enabled ? "enable" : "disable";
        if (!_gate.IsWriteAllowed)
            return await RecordAsync(Blocked(control, action, reason, origin, identity), cancellationToken)
                .ConfigureAwait(false);

        int beforeState = 0;
        int beforeTrigger = 0;
        var baselineCaptured = false;
        try
        {
            var support = await _platform.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!support.SupportedMachine)
                return await RecordAsync(Failed(control, action,
                    "Refusing EC radio write on an unverified machine model.", reason, origin, identity), cancellationToken)
                    .ConfigureAwait(false);

            var baseline = await _reader.ReadSnapshotAsync(
                [DeviceStateAddress, RadioTriggerAddress], cancellationToken).ConfigureAwait(false);
            var state = baseline.Fields.FirstOrDefault(f => f.Address == DeviceStateAddress);
            var trigger = baseline.Fields.FirstOrDefault(f => f.Address == RadioTriggerAddress);
            if (state is not { Ok: true } || trigger is not { Ok: true })
            {
                var why = state?.Error ?? trigger?.Error ?? "EC baseline was incomplete.";
                return await RecordAsync(Failed(control, action,
                    $"Refusing radio write without a complete EC baseline: {why}", reason, origin, identity), cancellationToken)
                    .ConfigureAwait(false);
            }

            beforeState = state.Value & 0xFF;
            beforeTrigger = trigger.Value & 0xFF;
            baselineCaptured = true;
            var targetState = enabled
                ? beforeState | WirelessRadioMask
                : beforeState & ~WirelessRadioMask;
            targetState &= 0xFF;
            var targetTrigger = (beforeTrigger | WirelessRadioMask) & 0xFF;

            var stateWrite = await _writer.WriteAsync(DeviceStateAddress, targetState, cancellationToken)
                .ConfigureAwait(false);
            if (!stateWrite.Ok || !RadioBitsMatch(stateWrite.Value, enabled))
                return await RollBackAndFailAsync(beforeState, beforeTrigger, control, action,
                    "EC rejected the radio state byte.", reason, origin, identity, cancellationToken)
                    .ConfigureAwait(false);

            // 0x7A1 is a pulse/pending byte. Its radio bits are consumed by the EC,
            // so exact read-back is neither required nor expected.
            var triggerWrite = await _writer.WriteAsync(RadioTriggerAddress, targetTrigger, cancellationToken)
                .ConfigureAwait(false);
            if (!triggerWrite.Ok)
                return await RollBackAndFailAsync(beforeState, beforeTrigger, control, action,
                    "EC rejected the radio trigger byte.", reason, origin, identity, cancellationToken)
                    .ConfigureAwait(false);

            var platformResult = await _platform.ReconcileWirelessRadiosAsync(enabled, cancellationToken)
                .ConfigureAwait(false);
            if (!platformResult.Executed && platformResult.Verified is false)
                return await RollBackAndFailAsync(beforeState, beforeTrigger, control, action,
                    platformResult.Message ?? "Windows radio reconciliation failed.",
                    reason, origin, identity, cancellationToken).ConfigureAwait(false);

            var settled = await _reader.ReadSnapshotAsync([DeviceStateAddress], cancellationToken)
                .ConfigureAwait(false);
            var after = settled.Fields.FirstOrDefault();
            if (after is not { Ok: true } || !RadioBitsMatch(after.Value, enabled))
                return await RollBackAndFailAsync(beforeState, beforeTrigger, control, action,
                    "Radio state did not settle to the requested EC value.",
                    reason, origin, identity, cancellationToken).ConfigureAwait(false);

            var ok = new MachineControlResult(
                DateTimeOffset.UtcNow, control, action, MachineControlOutcome.Verified,
                platformResult.Message,
                Before: $"0x{beforeState:X2}", After: $"0x{after.Value & 0xFF:X2}",
                Reason: reason, Origin: origin, Identity: identity);
            return await RecordAsync(ok, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // If a state byte may have been touched, best-effort rollback through the
            // same state+trigger sequence. Never mask the original error.
            var rollback = baselineCaptured
                ? await RestoreWirelessAsync(beforeState, beforeTrigger, CancellationToken.None).ConfigureAwait(false)
                : null;
            var message = rollback is null ? ex.Message : $"{ex.Message} {rollback.Message}";
            return await RecordAsync(Failed(control, action, message, reason, origin, identity), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public ValueTask<MachineControlResult> SetTouchpadEnabledAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
        => RunPlatformMutationAsync(
            "touchpad", enabled ? "enable" : "disable", reason,
            ct => _platform.SetTouchpadEnabledAsync(enabled, ct),
            cancellationToken, origin, identity);

    public ValueTask<MachineControlResult> SetWebcamEnabledAsync(
        bool enabled, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
        => RunPlatformMutationAsync(
            "webcam", enabled ? "enable" : "disable", reason,
            ct => _platform.SetWebcamEnabledAsync(enabled, ct),
            cancellationToken, origin, identity);

    public ValueTask<MachineControlResult> SetBrightnessAsync(
        int percent, string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
    {
        if (percent is < 0 or > 100)
            return RecordAsync(Failed("brightness", "set",
                "Brightness must be between 0 and 100.", reason, origin, identity), cancellationToken);

        return RunPlatformMutationAsync(
            "brightness", $"set:{percent}", reason,
            ct => _platform.SetBrightnessAsync((byte)percent, ct),
            cancellationToken, origin, identity);
    }

    public ValueTask<MachineControlResult> TurnOffDisplayAsync(
        string reason, CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
        => RunPlatformMutationAsync(
            "display", "turn-off", reason,
            ct => _platform.TurnOffDisplayAsync(ct),
            cancellationToken, origin, identity);

    private async ValueTask<MachineControlResult> RunPlatformMutationAsync(
        string control, string action, string reason,
        Func<CancellationToken, ValueTask<PlatformMutationResult>> mutation,
        CancellationToken cancellationToken, string? origin, string? identity)
    {
        if (!_gate.IsWriteAllowed)
            return await RecordAsync(Blocked(control, action, reason, origin, identity), cancellationToken)
                .ConfigureAwait(false);

        try
        {
            var support = await _platform.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!support.SupportedMachine)
                return await RecordAsync(Failed(control, action,
                    "Refusing hardware control on an unverified machine model.", reason, origin, identity), cancellationToken)
                    .ConfigureAwait(false);

            var platform = await mutation(cancellationToken).ConfigureAwait(false);
            var outcome = platform.Verified switch
            {
                true => MachineControlOutcome.Verified,
                null when platform.Executed => MachineControlOutcome.Requested,
                _ => MachineControlOutcome.Failed,
            };
            var result = new MachineControlResult(
                DateTimeOffset.UtcNow, control, action, outcome, platform.Message,
                Reason: reason, Origin: origin, Identity: identity);
            return await RecordAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await RecordAsync(Failed(control, action, ex.Message, reason, origin, identity), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<MachineControlResult> RollBackAndFailAsync(
        int beforeState, int beforeTrigger, string control, string action, string error,
        string reason, string? origin, string? identity, CancellationToken cancellationToken)
    {
        var rollback = await RestoreWirelessAsync(
            beforeState, beforeTrigger, CancellationToken.None).ConfigureAwait(false);
        return await RecordAsync(Failed(control, action,
            $"{error} {rollback.Message}", reason, origin, identity,
            Before: $"0x{beforeState:X2}"), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WirelessRollbackResult> RestoreWirelessAsync(
        int beforeState, int beforeTrigger, CancellationToken cancellationToken)
    {
        try
        {
            var stateWrite = await _writer.WriteAsync(
                    DeviceStateAddress, beforeState & 0xFF, cancellationToken)
                .ConfigureAwait(false);
            if (!stateWrite.Ok || (stateWrite.Value & 0xFF) != (beforeState & 0xFF))
                return WirelessRollbackResult.Failed("EC rejected the original radio state byte.");

            var triggerWrite = await _writer.WriteAsync(RadioTriggerAddress,
                (beforeTrigger | WirelessRadioMask) & 0xFF, cancellationToken).ConfigureAwait(false);
            if (!triggerWrite.Ok)
                return WirelessRollbackResult.Failed("EC rejected the rollback trigger byte.");

            var platform = await _platform.ReconcileWirelessRadiosAsync(
                RadioBitsMatch(beforeState, enabled: true), cancellationToken).ConfigureAwait(false);
            if (!platform.Executed || platform.Verified is false)
                return WirelessRollbackResult.Failed(
                    platform.Message ?? "Windows radio reconciliation failed during rollback.");

            var snapshot = await _reader.ReadSnapshotAsync(
                [DeviceStateAddress], cancellationToken).ConfigureAwait(false);
            var restored = snapshot.Fields.FirstOrDefault();
            if (restored is not { Ok: true } || (restored.Value & 0xFF) != (beforeState & 0xFF))
                return WirelessRollbackResult.Failed(
                    restored?.Error ?? "EC radio-state read-back did not match the baseline.");

            return new WirelessRollbackResult(true,
                "Rollback verified: the original EC radio state was restored and Windows was reconciled.");
        }
        catch (Exception ex)
        {
            return WirelessRollbackResult.Failed(ex.Message);
        }
    }

    private sealed record WirelessRollbackResult(bool Verified, string Message)
    {
        public static WirelessRollbackResult Failed(string detail)
            => new(false, $"Rollback was attempted but could not be verified: {detail}");
    }

    private static bool RadioBitsMatch(int value, bool enabled)
        => ((value & WirelessRadioMask) == WirelessRadioMask) == enabled;

    private async ValueTask<MachineControlResult> RecordAsync(
        MachineControlResult result, CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static MachineControlResult Blocked(
        string control, string action, string reason, string? origin, string? identity)
        => new(DateTimeOffset.UtcNow, control, action, MachineControlOutcome.Blocked,
            "Denied: hardware write gate is closed.", Reason: reason, Origin: origin, Identity: identity);

    private static MachineControlResult Failed(
        string control, string action, string message, string reason,
        string? origin, string? identity, string? Before = null)
        => new(DateTimeOffset.UtcNow, control, action, MachineControlOutcome.Failed,
            message, Before: Before, Reason: reason, Origin: origin, Identity: identity);

    private static string? JoinErrors(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return first + " " + second;
    }
}
