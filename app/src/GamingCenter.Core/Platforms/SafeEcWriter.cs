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

        // --- Verify read-back ---
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
