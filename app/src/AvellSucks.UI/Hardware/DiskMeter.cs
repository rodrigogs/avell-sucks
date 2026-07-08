using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AvellSucks.UI.Hardware;

/// <summary>
/// Reads fixed-drive capacity off the UI thread. <see cref="DriveInfo"/> does
/// synchronous disk I/O and can block on a spun-down or flaky disk, so sampling
/// is wrapped in <see cref="Task.Run(System.Func{object})"/> and awaited — the
/// dashboard never stalls on it. Storage changes slowly, so callers sample at a
/// low cadence (~10 s), unlike the 1 Hz sensor pump.
/// </summary>
public sealed class DiskMeter
{
    /// <summary>Snapshot every ready, fixed drive's capacity. Never throws.</summary>
    public Task<IReadOnlyList<DriveUsage>> SampleAsync() => Task.Run<IReadOnlyList<DriveUsage>>(() =>
    {
        var list = new List<DriveUsage>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return list; }

        foreach (var d in drives)
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                long total = d.TotalSize;
                if (total <= 0) continue;
                long free = d.TotalFreeSpace;
                // Name is like "C:\" — trim to "C:".
                list.Add(new DriveUsage(d.Name.TrimEnd('\\'), total - free, free, total));
            }
            catch { /* skip a drive that throws mid-enumeration (access denied, transient) */ }
        }
        return list;
    });
}

/// <summary>One fixed drive's capacity snapshot, in bytes.</summary>
public readonly record struct DriveUsage(string Name, long UsedBytes, long FreeBytes, long TotalBytes)
{
    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
}
