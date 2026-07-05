using GamingCenter.Core.Models;

namespace GamingCenter.Core;

/// <summary>
/// Minimal event source for MVP telemetry.
/// </summary>
public interface IEventPublisher
{
    Task<SystemInfo> NextAsync(CancellationToken ct);
}
