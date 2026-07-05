using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace GamingCenter.UI.Hardware;

/// <summary>
/// Reads CPU metrics that LibreHardwareMonitor doesn't expose on this platform,
/// using Windows performance counters + registry:
/// - Temperature, the way the OEM Gaming Center does (Thermal Zone counter,
///   Kelvin → Celsius).
/// - Effective clock, the way Task Manager does (base clock × "% Processor
///   Performance").
/// Package power (RAPL) needs ring-0 MSR access and is not covered here.
/// </summary>
public sealed class CpuThermalZone : IDisposable
{
    private PerformanceCounter? _counter;
    private bool _tried;

    private PerformanceCounter? _perfPct;
    private bool _triedClock;
    private double _baseClockMhz;

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

    /// <summary>
    /// Effective CPU clock in MHz: base clock scaled by the current performance
    /// percentage (which exceeds 100% under turbo). Null if unavailable.
    /// </summary>
    public double? ReadClockMhz()
    {
        EnsureClock();
        if (_perfPct is null || _baseClockMhz <= 0) return null;
        try
        {
            double pct = _perfPct.NextValue();       // ~100 = base, >100 = turbo
            double mhz = _baseClockMhz * pct / 100.0;
            return mhz is > 100 and < 12000 ? Math.Round(mhz) : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureClock()
    {
        if (_triedClock) return;
        _triedClock = true;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("~MHz") is int mhz) _baseClockMhz = mhz;

            // "% Processor Performance" tracks the real frequency ratio incl. turbo.
            _perfPct = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _perfPct.NextValue(); // discard first (always 0)
        }
        catch
        {
            _perfPct = null;
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
        _perfPct?.Dispose();
        _perfPct = null;
    }
}
