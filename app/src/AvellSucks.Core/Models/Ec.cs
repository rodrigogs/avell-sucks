namespace AvellSucks.Core.Models;

/// <summary>
/// Read-only representation of one EC field read from WMI.
/// </summary>
public sealed record EcField(int Address, string HexAddress, int Value, string ValueHex, bool Ok, string? Error);

/// <summary>
/// Full read-only EC snapshot with one or more fields.
/// </summary>
public sealed record EcSnapshot(DateTimeOffset Timestamp, IReadOnlyList<EcField> Fields);

/// <summary>
/// Interpreted fan-mode view derived from EC fields.
/// </summary>
public sealed record FanMode(string Source, DateTimeOffset Timestamp, int RawValue, bool IsAuto, string Description);
