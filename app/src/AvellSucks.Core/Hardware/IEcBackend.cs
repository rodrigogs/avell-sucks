using AvellSucks.Core.Models;

namespace AvellSucks.Core.Hardware;

/// <summary>
/// Abstraction for read-only EC access.
/// </summary>
public interface IEcBackend
{
    /// <summary>
    /// Reads a set of EC fields from the firmware.
    /// </summary>
    ValueTask<EcSnapshot> ReadSnapshotAsync(IReadOnlyList<int> addresses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interprets the raw EC values as a current fan mode.
    /// </summary>
    ValueTask<FanMode?> InterpretFanModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current power-limit profile state from EC registers if supported.
    /// Returns null when the hardware surface does not expose power limits.
    /// </summary>
    ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken cancellationToken = default);
}
