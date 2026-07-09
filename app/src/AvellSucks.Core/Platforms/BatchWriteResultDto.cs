using System.Collections.Generic;
using System.Linq;

namespace AvellSucks.Core.Platforms;

/// <summary>
/// Normalized result of a batch of EC writes (e.g. a fan curve or a power
/// profile): the aggregate allowed/executed/verified plus the per-write results.
/// Shared by the fan-curve and power-profile endpoints so the shape and the
/// aggregation rule live in one place.
/// </summary>
public sealed record BatchWriteResultDto(
    bool Allowed, bool Executed, bool Verified, string? Error, IReadOnlyList<EcWriteResult> Results)
{
    /// <summary>Neutral result for "nothing to write" — treated as success.</summary>
    public static BatchWriteResultDto Empty { get; } = From([]);

    /// <summary>
    /// Aggregate a set of write results. An empty set is success (all() is true on
    /// empty); the surfaced error is the last non-empty one.
    /// </summary>
    public static BatchWriteResultDto From(IReadOnlyList<EcWriteResult> results)
    {
        var allowed = results.All(r => r.Allowed);
        var executed = results.All(r => r.Executed);
        var verified = results.All(r => r.Verified);
        var error = results.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Error))?.Error;
        return new BatchWriteResultDto(allowed, executed, verified, error, results);
    }
}
