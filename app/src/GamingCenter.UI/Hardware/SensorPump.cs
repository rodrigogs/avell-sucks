using System;
using System.Windows.Threading;

namespace GamingCenter.UI.Hardware;

/// <summary>
/// Owns a single <see cref="HardwareMonitor"/> and a 1 Hz <see cref="DispatcherTimer"/>,
/// and raises <see cref="Tick"/> with the latest <see cref="Telemetry"/> each second
/// (null when sensors are unavailable). Views subscribe instead of each opening
/// their own ring-0 monitor — one owner, no duplicated poll/dispose lifecycle,
/// no EC/driver contention from two monitors.
/// </summary>
public sealed class SensorPump : IDisposable
{
    private readonly HardwareMonitor? _monitor;
    private readonly DispatcherTimer _timer;

    /// <summary>Fires on the UI thread every second with a fresh snapshot, or null if unavailable.</summary>
    public event Action<Telemetry?>? Tick;

    /// <summary>True when the sensor backend opened successfully.</summary>
    public bool SensorsAvailable => _monitor is not null;

    public SensorPump()
    {
        try { _monitor = new HardwareMonitor(); }
        catch { _monitor = null; }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Emit();
    }

    /// <summary>Start polling and emit one immediate sample.</summary>
    public void Start()
    {
        _timer.Start();
        Emit();
    }

    private void Emit()
    {
        Telemetry? t = null;
        if (_monitor is not null)
        {
            try { t = _monitor.GetTelemetry(); }
            catch { t = null; }
        }
        Tick?.Invoke(t);
    }

    public void Dispose()
    {
        _timer.Stop();
        _monitor?.Dispose();
    }
}
