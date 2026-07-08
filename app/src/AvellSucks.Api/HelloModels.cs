namespace AvellSucks.Api.Models;

/// <summary>
/// Request payload for hello-world automation.
/// </summary>
public sealed record HelloRequest(string? Name = null);

/// <summary>
/// Response contract for hello-world automation.
/// </summary>
public sealed record HelloResponse(string Message, DateTimeOffset RequestedAt);
