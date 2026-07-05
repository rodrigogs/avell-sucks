using System;
using System.Diagnostics;
using System.Linq;

namespace GamingCenter.UI.Hardware;

/// <summary>
/// Reads CPU temperature the same way the OEM Gaming Center does — via the
/// Windows "Thermal Zone Information / Temperature" performance counter
/// (reported in Kelvin, converted to Celsius). This is the source that matches
/// the original app's readout on this platform, where LibreHardwareMonitor
/// exposes no CPU temperature sensor.
/// </summary>
public sealed class CpuThermalZone : IDisposable
{
    private PerformanceCounter? _counter;
    private bool _tried;

    /// <summary>Current CPU temperature in °C, or null if the counter is unavailable.</summary>
    public double? ReadCelsius()
    {
        EnsureCounter();
        if (_counter is null) return null;
        try
        {
            double kelvin = _counter.NextValue();
            double c = kelvin - 273.15;
            // Thermal-zone counters occasionally report absurd values before warmup.
            return c is > 0 and < 125 ? Math.Round(c, 0) : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureCounter()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var cat = new PerformanceCounterCategory("Thermal Zone Information");
            var instances = cat.GetInstanceNames();
            if (instances.Length == 0) return;

            // Prefer a CPU/processor thermal zone; fall back to the first.
            string instance = instances.FirstOrDefault(n =>
                                   n.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                   n.Contains("TZ", StringComparison.OrdinalIgnoreCase) ||
                                   n.Contains("PROC", StringComparison.OrdinalIgnoreCase))
                               ?? instances[0];

            _counter = new PerformanceCounter("Thermal Zone Information", "Temperature", instance, readOnly: true);
            _counter.NextValue(); // discard the first sample (always 0/garbage)
        }
        catch
        {
            _counter = null;
        }
    }

    public void Dispose()
    {
        _counter?.Dispose();
        _counter = null;
    }
}
