using AvellSucks.Core.Hardware;

namespace AvellSucks.Core.Platforms;

/// <summary>
/// Assembles the portable half of the EC safe-write pipeline (allowlist +
/// <see cref="SafeEcWriter"/>) around a caller-supplied backend, gate and audit
/// sink. Both front-ends build the same graph: the UI hand-rolls it in its
/// static composition root, the Server wires it through DI — this keeps the one
/// shared assembly step in one place so they can't drift.
///
/// The backend (a Windows-specific WmiEcBackend), the gate policy, and the audit
/// destination are legitimately environment-specific, so they stay caller-owned.
/// </summary>
public static class EcPipeline
{
    /// <summary>Build a <see cref="SafeEcWriter"/> over the given backend/gate/audit.</summary>
    public static SafeEcWriter BuildWriter(
        IEcBackend reader, IEcWriter writer, WriteGate gate, IWriteAuditLog audit)
        => new(gate, new EcWriteAllowlist(), reader, writer, audit);
}
