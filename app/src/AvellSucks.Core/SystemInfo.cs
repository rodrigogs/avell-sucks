namespace AvellSucks.Core.Models;

public sealed record SystemInfo(
    DateTimeOffset Timestamp,
    long TotalMemoryBytes,
    long FreeMemoryBytes,
    int ActiveProcessCount,
    IReadOnlyList<ProcessInfo> Processes
);
