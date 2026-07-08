using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AvellSucks.UI.Hardware;

/// <summary>
/// One-shot diagnostic: enumerate every LibreHardwareMonitor sensor on this
/// machine and write it to a file, so sensor selection can be matched exactly
/// against real hardware instead of guessed by name.
/// </summary>
public static class SensorDump
{
    public static void Write(string path)
    {
        using var monitor = new HardwareMonitor();
        // Two passes: some sensors only populate after a second update tick.
        monitor.Update();
        Thread.Sleep(1000);
        monitor.Update();
        var snap = monitor.GetSnapshot();

        var sb = new StringBuilder();
        sb.AppendLine($"# Sensor dump {DateTime.Now:O}");
        sb.AppendLine($"# {snap.Sensors.Count} sensors");
        sb.AppendLine();

        foreach (var group in snap.Sensors
                     .GroupBy(s => s.Hardware)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"=== {group.Key} ===");
            foreach (var s in group
                         .OrderBy(s => s.SensorType, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var max = s.Max is null ? "" : $"  max={s.Max.Value.ToString(CultureInfo.InvariantCulture)}";
                sb.AppendLine($"  [{s.SensorType,-12}] {s.Name,-32} = {s.Value.ToString(CultureInfo.InvariantCulture)}{max}");
            }
            sb.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
