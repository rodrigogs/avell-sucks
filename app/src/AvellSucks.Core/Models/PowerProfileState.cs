namespace AvellSucks.Core.Models;

/// <summary>
/// Snapshot of the current power-limit state read from EC registers.
/// </summary>
public sealed record PowerProfileState(
    DateTimeOffset Timestamp,
    int Pl1Watts,
    int Pl2Watts,
    int TauSeconds,
    bool Supported,
    string? Error);
