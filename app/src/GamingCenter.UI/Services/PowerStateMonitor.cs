using System;
using System.Windows.Threading;

namespace GamingCenter.UI.Services;

/// <summary>
/// Reconciler for the Performance tab — the Power-side twin of FanStateMonitor.
/// Polls the ACTIVE WINDOWS POWER PLAN (the authoritative, always-readable
/// source) and raises <see cref="ExternalModeChanged"/> when it changes outside
/// our app (another tool, the OEM app, or a Windows power-mode switch).
///
/// Local writes call <see cref="NoteLocalWrite"/> so our own mode switch isn't
/// reported back as external. Runs on a WPF DispatcherTimer (UI thread).
/// </summary>
public sealed class PowerStateMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private PerformanceMode? _baseline;
    private bool _busy;
    private bool _disposed;

    /// <summary>Raised (UI thread) when the active power plan changed externally.</summary>
    public event Action<PerformanceMode>? ExternalModeChanged;

    public PowerStateMonitor(double intervalSeconds = 3.0)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
        _timer.Tick += OnTick;
    }

    public void NoteLocalWrite(PerformanceMode mode) => _baseline = mode;

    /// <summary>
    /// Seed the baseline from the SAME source the tick uses (the active Windows
    /// scheme), off the UI thread, so the first tick doesn't fire a spurious
    /// "changed on device" for a plan that was already active at load. Call before
    /// Start(). If the active scheme isn't one we map, leaves the baseline unset
    /// and the first tick establishes it silently.
    /// </summary>
    public async System.Threading.Tasks.Task SeedBaselineAsync()
    {
        try { _baseline = await System.Threading.Tasks.Task.Run(() => WindowsPowerPlan.ActiveMode()).ConfigureAwait(true); }
        catch { /* leave unset; first tick seeds silently */ }
    }

    public void Start() { if (!_disposed) _timer.Start(); }
    public void Stop() => _timer.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            // powercfg spawns subprocesses — NEVER run it on the UI thread or it
            // freezes rendering (gauges stop, telemetry stalls). Poll off-thread;
            // only the (cheap) comparison + event marshal back here.
            var mode = await Task.Run(() => WindowsPowerPlan.ActiveMode()).ConfigureAwait(true);
            if (_disposed || mode is null) return; // not one of our mapped schemes

            if (_baseline is null) { _baseline = mode; return; }
            if (mode != _baseline)
            {
                _baseline = mode;
                ExternalModeChanged?.Invoke(mode.Value); // back on UI thread (await true)
            }
        }
        catch { /* transient — retry next tick */ }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
