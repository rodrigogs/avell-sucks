using System.Text.Json;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;

namespace AvellSucks.Core.Platforms;

/// <summary>
/// Orchestrates a safe EC write: gate check, allowlist check,
/// before-snapshot, execute write, after-snapshot, verify read-back,
/// and rollback-on-mismatch — all audited.
/// </summary>
public sealed class SafeEcWriter
{
    private readonly WriteGate _gate;
    private readonly EcWriteAllowlist _allowlist;
    private readonly IEcBackend _reader;
    private readonly IEcWriter _writer;
    private readonly IWriteAuditLog _audit;

    public SafeEcWriter(
        WriteGate gate,
        EcWriteAllowlist allowlist,
        IEcBackend reader,
        IEcWriter writer,
        IWriteAuditLog audit)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// Attempts a single EC register write under all safety constraints.
    /// Does not throw for gate/allowlist denials, a failed pre-read, or backend
    /// write/rollback errors — every outcome (denied, no-baseline, write-threw,
    /// read-back mismatch, verified) is returned as an <see cref="EcWriteResult"/>
    /// and audited. Callers (incl. UI async-void handlers) can rely on that.
    /// </summary>
    public async ValueTask<EcWriteResult> TryWriteAsync(
        int address, int value, string reason,
        CancellationToken cancellationToken = default,
        string? origin = null, string? identity = null)
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new EcWriteAttempt(
            now, address, $"0x{address:X}",
            value, $"0x{value:X}", reason, origin, identity);

        // --- Gate ---
        if (!_gate.IsWriteAllowed)
        {
            var denied = new EcWriteResult(
                attempt, Allowed: false, Executed: false, Verified: false,
                Before: null, After: null, RolledBackTo: null,
                RollbackAttempted: false,
                Error: "Denied: write gate is closed.");
            await _audit.RecordAsync(denied, cancellationToken).ConfigureAwait(false);
            return denied;
        }

        // --- Allowlist ---
        if (!_allowlist.IsAllowed(address, value))
        {
            var denied = new EcWriteResult(
                attempt, Allowed: false, Executed: false, Verified: false,
                Before: null, After: null, RolledBackTo: null,
                RollbackAttempted: false,
                Error: "Denied: (address, value) not in EC write allowlist.");
            await _audit.RecordAsync(denied, cancellationToken).ConfigureAwait(false);
            return denied;
        }

        // --- Before snapshot ---
        var beforeSnap = await _reader.ReadSnapshotAsync([address], cancellationToken)
            .ConfigureAwait(false);
        var before = beforeSnap.Fields[0];

        // Refuse to write without a known prior state: if the pre-read failed,
        // `before.Value` is a bogus 0, and a later read-back mismatch would roll
        // the register back to 0 (for the fan byte 0x751 that silently forces
        // auto) while the audit records a "before" that never existed.
        if (!before.Ok)
        {
            var noBaseline = new EcWriteResult(
                attempt, Allowed: true, Executed: false, Verified: false,
                Before: before, After: null, RolledBackTo: null,
                RollbackAttempted: false,
                Error: $"Refusing to write without a known prior state: "
                       + $"pre-read of 0x{address:X} failed ({before.Error ?? "unknown"}).");
            await _audit.RecordAsync(noBaseline, cancellationToken).ConfigureAwait(false);
            return noBaseline;
        }

        // --- Execute write ---
        // Any backend exception (incl. transient WMI errors WmiEcBackend wraps as
        // InvalidOperationException) becomes a Failed result, NOT a throw: UI
        // callers await this from async void handlers, so a throw would crash the
        // process instead of surfacing a "Didn't apply" toast.
        EcField after;
        try
        {
            after = await _writer.WriteAsync(address, value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failed = new EcWriteResult(
                attempt, Allowed: true, Executed: false, Verified: false,
                Before: before, After: null, RolledBackTo: null,
                RollbackAttempted: false,
                Error: $"Write threw: {ex.Message}");
            await _audit.RecordAsync(failed, cancellationToken).ConfigureAwait(false);
            return failed;
        }

        // --- Verify read-back, with settle + retry ---
        // Two hardware realities on control registers (0x751 fan byte), both
        // confirmed by probing this EC:
        //   1) TRANSIENT bits: right after a write the byte may read back with
        //      firmware status bits set (e.g. 0x51 = boost 0x40 + 0x11), settling
        //      to the exact value within ~½s.
        //   2) SWALLOWED first write: leaving Boost (0x40 → another mode) is
        //      intermittently ignored on the first write and needs a second one —
        //      the value never latches otherwise, even after seconds.
        // So: if the read-back doesn't match, settle then RE-WRITE and re-read, up
        // to a few attempts, before giving up and rolling back. A genuine
        // rejection (value the EC refuses) still fails after the retries. The OEM
        // app doesn't verify at all; this keeps the safety net without the
        // false/incomplete failures the user hit ("try more than once").
        // Backoff schedule (ms). Leaving Boost (0x40) is the slow case: the EC
        // stays latched on 0x40 and ignores writes for up to ~1.5-2s before it
        // accepts the next mode (probed on hardware). Re-write + wait with growing
        // gaps until it takes, total budget ~2.7s, before giving up.
        int[] backoffMs = [150, 300, 500, 800, 900];
        foreach (var wait in backoffMs)
        {
            if (after.Ok && after.Value == value) break;
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            try
            {
                var recheck = await _reader.ReadSnapshotAsync([address], cancellationToken)
                    .ConfigureAwait(false);
                after = recheck.Fields[0];
                if (after.Ok && after.Value == value) break; // settled to the target
                // Re-issue the write; the EC swallows writes while a prior transition
                // (esp. Boost exit) is still settling.
                after = await _writer.WriteAsync(address, value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A transient read/write error mid-retry must not crash: keep the
                // last `after` and let the loop try again, or fall through to the
                // mismatch/rollback path below when the budget is exhausted.
            }
        }

        if (!after.Ok || after.Value != value)
        {
            // Rollback to the pre-write value. A throw here must not escape (same
            // async-void-crash reason as the write) — record it as a rollback that
            // couldn't be confirmed.
            EcField? rolledBack = null;
            string rollbackNote;
            try
            {
                rolledBack = await RollbackAsync(address, before.Value, cancellationToken)
                    .ConfigureAwait(false);
                rollbackNote = $"Rolled back to {before.ValueHex}.";
            }
            catch (Exception ex)
            {
                rollbackNote = $"Rollback to {before.ValueHex} threw: {ex.Message}.";
            }
            var mismatch = new EcWriteResult(
                attempt, Allowed: true, Executed: true, Verified: false,
                Before: before, After: after, RolledBackTo: rolledBack,
                RollbackAttempted: true,
                Error: $"Read-back mismatch: expected 0x{value:X}, "
                       + $"got {(after.Ok ? after.ValueHex : "error")}. "
                       + rollbackNote);
            await _audit.RecordAsync(mismatch, cancellationToken).ConfigureAwait(false);
            return mismatch;
        }

        // --- Success ---
        var ok = new EcWriteResult(
            attempt, Allowed: true, Executed: true, Verified: true,
            Before: before, After: after, RolledBackTo: null,
            RollbackAttempted: false, Error: null);
        await _audit.RecordAsync(ok, cancellationToken).ConfigureAwait(false);
        return ok;
    }

    private async ValueTask<EcField> RollbackAsync(
        int address, int originalValue, CancellationToken cancellationToken)
    {
        // The original value is by definition in the allowlist (it was read
        // from a register we just proved we may write), but the rollback
        // value may be arbitrary. We skip the allowlist for rollback to
        // guarantee we always attempt to restore — the audit trail captures
        // the action.
        return await _writer.WriteAsync(address, originalValue, cancellationToken)
            .ConfigureAwait(false);
    }
}
