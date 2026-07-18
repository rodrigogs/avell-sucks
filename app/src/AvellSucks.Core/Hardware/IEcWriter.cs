using AvellSucks.Core.Models;

namespace AvellSucks.Core.Hardware;

/// <summary>
/// Contract for EC write operations. The Windows <c>WmiEcBackend</c> will
/// implement this alongside <see cref="IEcBackend"/>; tests use a recording
/// fake. Generic writes are gated by <see cref="Platforms.WriteGate"/> and
/// <see cref="Platforms.EcWriteAllowlist"/> through <c>SafeEcWriter</c>. A
/// model-specific orchestrator may call this primitive only when it owns an
/// equivalent gate, fixed address/mask policy, rollback and audit (for example
/// the volatile radio-trigger sequence).
/// </summary>
public interface IEcWriter
{
    /// <summary>
    /// Writes <paramref name="value"/> to EC register <paramref name="address"/>.
    /// Implementations must invoke the underlying firmware write (WMI
    /// <c>GetSetULong</c> on Windows) and return the value read-back from
    /// the register immediately after the write.
    /// </summary>
    ValueTask<EcField> WriteAsync(int address, int value, CancellationToken cancellationToken = default);
}
