using GamingCenter.Core.Models;

namespace GamingCenter.Core.Platforms;

/// <summary>
/// Audit record for one write attempt, regardless of outcome.
/// Serialised to the snapshot log so every privileged action leaves a trace.
/// </summary>
public sealed record EcWriteAttempt(
    DateTimeOffset Timestamp,
    int Address,
    string HexAddress,
    int RequestedValue,
    string RequestedValueHex,
    string Reason);

/// <summary>
/// Full record of a write attempt including verification and rollback state.
/// </summary>
public sealed record EcWriteResult(
    EcWriteAttempt Attempt,
    bool Allowed,            // gate + allowlist permitted the write
    bool Executed,           // the backend WriteAsync was actually called
    bool Verified,           // read-back matched requested value
    EcField? Before,         // EC value captured before write
    EcField? After,          // EC value captured after write (read-back)
    EcField? RolledBackTo,   // EC value after rollback write (if attempted)
    bool RollbackAttempted,
    string? Error);          // human-readable error if not Allowed or failed

/// <summary>
/// A sink for write-attempt audit lines. In production this appends to a
/// rolling JSON log; in tests it captures into an in-memory list.
/// </summary>
public interface IWriteAuditLog
{
    ValueTask RecordAsync(EcWriteResult result, CancellationToken cancellationToken = default);
}
