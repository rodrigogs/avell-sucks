using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AvellSucks.UI.Services;

/// <summary>
/// Base for the polling reconcilers that keep the UI in sync with the DEVICE,
/// reflecting changes made outside our app (the OEM app, the Fn key, another
/// tool). Owns the shared machinery — a UI-thread <see cref="DispatcherTimer"/>,
/// an overlap guard, the last-known baseline, the diff-then-signal loop, and
/// disposal. Subclasses supply only <see cref="ReadCurrentAsync"/> (how to read
/// the current device value) and, if the type isn't equatable by default,
/// <see cref="AreEqual"/>.
///
/// Local writes call <see cref="NoteLocalWrite"/> so our own actuation isn't
/// reported back as an external change. Runs on the UI thread, so
/// <see cref="ExternalChanged"/> subscribers may touch controls directly.
///
/// This is the polling half; a WMI push watcher can layer on top later as a
/// low-latency trigger, with polling staying the authoritative reconciliation.
/// </summary>
public abstract class StateReconciler<T> : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly IEqualityComparer<T> _comparer;
    private bool _busy;       // guard against overlapping async ticks
    private bool _disposed;

    private T _baseline = default!;   // last value considered "ours"; valid only when _hasBaseline
    private bool _hasBaseline;

    /// <summary>True while a local write is settling; ticks are skipped.</summary>
    protected bool Suspended;

    /// <summary>Set the baseline to a known value (subclass seeding / resume).</summary>
    protected void SetBaseline(T value) { _baseline = value; _hasBaseline = true; }

    /// <summary>Raised (on the UI thread) when the device value changed externally.</summary>
    public event Action<T>? ExternalChanged;

    protected StateReconciler(double intervalSeconds, IEqualityComparer<T>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Read the current device value. Return <c>ok=false</c> when it's unreadable
    /// this tick or isn't a value we track (the tick is then skipped). A tuple —
    /// not <c>T?</c> — because T is both a reference type (fan mode string) and a
    /// value type (PerformanceMode), which nullable annotations can't span
    /// uniformly without a struct/class constraint.
    /// </summary>
    protected abstract Task<(bool ok, T value)> ReadCurrentAsync();

    /// <summary>Value equality for the baseline diff. Override for case-insensitive etc.</summary>
    protected virtual bool AreEqual(T a, T b) => _comparer.Equals(a, b);

    /// <summary>Record a value we just wrote (or read) so the poll won't flag it as external.</summary>
    public void NoteLocalWrite(T value) => SetBaseline(value);

    public void Start() { if (!_disposed) _timer.Start(); }
    public void Stop() => _timer.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_busy || _disposed || Suspended) return;
        _busy = true;
        try
        {
            var (ok, current) = await ReadCurrentAsync().ConfigureAwait(true);
            if (_disposed || !ok) return; // unreadable / not mapped — leave state
            if (!_hasBaseline) { SetBaseline(current); return; } // first read seeds silently
            if (!AreEqual(current, _baseline))
            {
                SetBaseline(current);
                ExternalChanged?.Invoke(current);
            }
        }
        catch { /* transient read error — skip this tick, retry next */ }
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
