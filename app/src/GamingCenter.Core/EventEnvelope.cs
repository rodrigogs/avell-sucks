using System.Text.Json;

namespace GamingCenter.Core.Events;

/// <summary>
/// Minimal event envelope for telemetry/events.
/// </summary>
public sealed record EventEnvelope<T>(
    string Id,
    string Type,
    DateTimeOffset Timestamp,
    T Data
) where T : class;
