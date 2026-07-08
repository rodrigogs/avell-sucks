namespace AvellSucks.Core.Models;

public sealed record GameInfo(string Id, string Name, string? Description, DateTimeOffset FirstSeenUtc);
