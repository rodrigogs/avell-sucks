using System.Diagnostics;

namespace AvellSucks.Core.Models;

public sealed record ProcessInfo(
    int Id,
    string Name,
    string? Path,
    long? MemoryBytes,
    double? CpuPercent
);
