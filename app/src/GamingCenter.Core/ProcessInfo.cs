using System.Diagnostics;

namespace GamingCenter.Core.Models;

public sealed record ProcessInfo(
    int Id,
    string Name,
    string? Path,
    long? MemoryBytes,
    double? CpuPercent
);
