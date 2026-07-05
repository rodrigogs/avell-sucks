using System.Collections.Generic;
using System.Linq;
using GamingCenter.Core.Platforms;

namespace GamingCenter.Core.Models;

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

/// <summary>
/// Normalized write result for power-profile mutations.
/// </summary>
public sealed record PowerWriteResultDto(
    bool Allowed,
    bool Executed,
    bool Verified,
    string? Error,
    IReadOnlyList<EcWriteResult> Results)
{
    public static PowerWriteResultDto Empty { get; } =
        new(false, false, false, "nothing changed", []);

    public static PowerWriteResultDto From(IReadOnlyList<EcWriteResult> results)
    {
        var allowed = results.Count == 0 || results.All(r => r.Allowed);
        var executed = results.Count == 0 || results.All(r => r.Executed);
        var verified = results.Count == 0 || results.All(r => r.Verified);
        var error = results.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Error))?.Error;
        return new PowerWriteResultDto(allowed, executed, verified, error, results);
    }
}
