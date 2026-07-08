using System.Text.Json;
using GamingCenter.Core.Hardware;
using GamingCenter.Core.Models;

namespace GamingCenter.Core.Platforms;

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
    /// Never throws for gate/allowlist denials — returns a denied
    /// <see cref="EcWriteResult"/>. Throws only for unexpected backend
    /// errors after the write has been authorised and logged.
    /// </summary>
    public async ValueTask<EcWriteResult> TryWriteAsync(
        int address, int value, string reason,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new EcWriteAttempt(
            now, address, $"0x{address:X}",
            value, $"0x{value:X}", reason);

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

        // --- Execute write ---
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
            throw;
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
            var recheck = await _reader.ReadSnapshotAsync([address], cancellationToken)
                .ConfigureAwait(false);
            after = recheck.Fields[0];
            if (after.Ok && after.Value == value) break; // settled to the target
            // Re-issue the write; the EC swallows writes while a prior transition
            // (esp. Boost exit) is still settling.
            after = await _writer.WriteAsync(address, value, cancellationToken).ConfigureAwait(false);
        }

        if (!after.Ok || after.Value != value)
        {
            // Rollback to the pre-write value
            var rolledBack = await RollbackAsync(address, before.Value, cancellationToken)
                .ConfigureAwait(false);
            var mismatch = new EcWriteResult(
                attempt, Allowed: true, Executed: true, Verified: false,
                Before: before, After: after, RolledBackTo: rolledBack,
                RollbackAttempted: true,
                Error: $"Read-back mismatch: expected 0x{value:X}, "
                       + $"got {(after.Ok ? after.ValueHex : "error")}. "
                       + $"Rolled back to {before.ValueHex}.");
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
