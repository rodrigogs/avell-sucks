using System;
using System.Windows.Threading;

namespace AvellSucks.UI.Hardware;

/// <summary>
/// Owns a single <see cref="HardwareMonitor"/> and a 1 Hz <see cref="DispatcherTimer"/>,
/// and raises <see cref="Tick"/> with the latest <see cref="Telemetry"/> each second
/// (null when sensors are unavailable). One pump is created by the window and shared
/// by the views that need telemetry — one owner, no duplicated poll/dispose lifecycle,
/// no EC/driver contention from two ring-0 monitors.
/// </summary>
public sealed class SensorPump : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private HardwareMonitor? _monitor;
    private bool _opened;
    private bool _disposed;

    /// <summary>Fires on the UI thread every second with a fresh snapshot, or null if unavailable.</summary>
    public event Action<Telemetry?>? Tick;

    /// <summary>True once the sensor backend opened successfully. Only meaningful after <see cref="Start"/>.</summary>
    public bool SensorsAvailable => _monitor is not null;

    public SensorPump()
    {
        // Construction stays cheap: the ring-0 monitor is opened lazily on Start(),
        // never in a field initializer. Opening it here would run _computer.Open()
        // on the UI thread during window construction — before the window is shown —
        // which blocks the message pump and leaves the window unrendered (Handle=0).
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Emit();
    }

    /// <summary>
    /// Idempotent. Opens the ring-0 monitor on first call (lazily, after the window
    /// is up), starts the 1 Hz timer, and emits one immediate sample. Safe to call
    /// from every subscribing view's Loaded handler.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        if (!_opened)
        {
            _opened = true;
            // Open the ring-0 monitor OFF the UI thread — LibreHardwareMonitor's
            // Open() loads a kernel driver and enumerates all hardware (~0.5-2s,
            // worst on first-ever launch). Doing it here on the dispatcher froze
            // the just-shown window. Start ticking only once it's ready; the
            // window paints immediately and telemetry fills in a beat later.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                HardwareMonitor? m = null;
                try { m = new HardwareMonitor(); } catch { m = null; }
                _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed) { m?.Dispose(); return; }
                    _monitor = m;
                    if (!_timer.IsEnabled) _timer.Start();
                    Emit();
                });
            });
            return;
        }

        if (!_timer.IsEnabled) _timer.Start();
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
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _monitor?.Dispose();
        _monitor = null;
    }
}
