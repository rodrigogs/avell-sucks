using System.Diagnostics;
using GamingCenter.Core.Models;

namespace GamingCenter.Api;

/// <summary>
/// Builds <see cref="SystemInfo"/> snapshots. Shared by SSE publisher
/// and REST endpoint to avoid duplicated Process.GetProcesses() calls.
/// </summary>
public static class SystemSnapshotBuilder
{
    private const int MaxProcesses = 200;

    /// <summary>
    /// Captures a point-in-time system snapshot with a single
    /// Process.GetProcesses() call.
    /// </summary>
    public static SystemInfo Build()
    {
        var allProcs = Process.GetProcesses();
        var topProcs = allProcs
            .OrderByDescending(p => p.WorkingSet64)
            .Take(MaxProcesses)
            .Select(static p => new ProcessInfo(
                Id: p.Id,
                Name: p.ProcessName,
                Path: null,
                MemoryBytes: p.WorkingSet64,
                CpuPercent: null))
            .ToArray();

        return new SystemInfo(
            Timestamp: DateTimeOffset.UtcNow,
            TotalMemoryBytes: GC.GetTotalMemory(false),
            FreeMemoryBytes: 0,
            ActiveProcessCount: allProcs.Length,
            Processes: topProcs);
    }
}
