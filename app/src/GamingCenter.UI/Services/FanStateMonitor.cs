using System;
using System.Windows.Threading;
using GamingCenter.Core.Hardware;

namespace GamingCenter.UI.Services;

/// <summary>
/// Reconciler that keeps the UI's idea of the fan mode in sync with the DEVICE —
/// reflecting changes made OUTSIDE our app (the OEM Gaming Center, or the
/// physical Fn fan key). DESIGN.md "Reactive Architecture Spec" §1(c): a periodic
/// EC re-read that diffs against the last-known value and raises
/// <see cref="ExternalModeChanged"/> when the device changed on its own.
///
/// Local writes call <see cref="NoteLocalWrite"/> so our own actuation is not
/// reported back as an external change. Runs on a WPF <see cref="DispatcherTimer"/>
/// (UI thread), so subscribers can touch controls directly.
///
/// This is the polling half. A WMI <c>AcpiTest_EventULong</c> watcher (push, fires
/// on the hardware Q-key) is the low-latency trigger layered on top later; polling
/// stays the authoritative reconciliation.
/// </summary>
public sealed class FanStateMonitor : IDisposable
{
    private readonly IEcBackend _backend;
    private readonly DispatcherTimer _timer;
    private string? _baseline;      // last mode we consider "ours" / already seen
    private bool _busy;             // guard against overlapping async ticks
    private bool _disposed;

    /// <summary>Raised (on the UI thread) when the device's fan mode changed externally.</summary>
    public event Action<string>? ExternalModeChanged;

    public FanStateMonitor(IEcBackend backend, double intervalSeconds = 1.5)
    {
        _backend = backend;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
        _timer.Tick += OnTick;
    }

    /// <summary>Record a value we just wrote (or read) so the poll won't flag it as external.</summary>
    public void NoteLocalWrite(string mode) => _baseline = mode;

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            var fanMode = await _backend.InterpretFanModeAsync().ConfigureAwait(true);
            if (fanMode is null) return; // unreadable; leave last state
            var mode = MapRaw(fanMode.RawValue);

            // First reading establishes the baseline silently.
            if (_baseline is null) { _baseline = mode; return; }

            if (!string.Equals(mode, _baseline, StringComparison.OrdinalIgnoreCase))
            {
                _baseline = mode;
                ExternalModeChanged?.Invoke(mode);
            }
        }
        catch
        {
            // Transient WMI hiccup — skip this tick, try again next.
        }
        finally
        {
            _busy = false;
        }
    }

    // Control-byte value → UI mode key (mirrors WmiFanService/FanController).
    private static string MapRaw(int raw) => raw switch
    {
        0 => "auto",
        64 => "boost",
        160 => "custom",
        129 => "L1", 130 => "L2", 131 => "L3", 132 => "L4", 133 => "L5",
        _ => "auto",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
