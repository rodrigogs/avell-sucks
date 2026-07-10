using System;
using System.Threading.Tasks;

namespace AvellSucks.UI.Services;

/// <summary>
/// Reconciler for the Performance tab — the Power-side twin of FanStateMonitor.
/// Polls the ACTIVE WINDOWS POWER PLAN (the authoritative, always-readable
/// source) and raises <see cref="ExternalModeChanged"/> when it changes outside
/// our app. See <see cref="StateReconciler{T}"/> for the shared machinery.
/// </summary>
public sealed class PowerStateMonitor : StateReconciler<PerformanceMode>
{
    /// <summary>Raised (UI thread) when the active power plan changed externally.</summary>
    public event Action<PerformanceMode>? ExternalModeChanged;

    public PowerStateMonitor(double intervalSeconds = 3.0) : base(intervalSeconds)
    {
        ExternalChanged += m => ExternalModeChanged?.Invoke(m);
    }

    // powercfg spawns subprocesses — NEVER on the UI thread (it freezes rendering).
    // Poll off-thread; the base marshals the cheap comparison + event back.
    protected override async Task<(bool ok, PerformanceMode value)> ReadCurrentAsync()
    {
        var mode = await Task.Run(() => WindowsPowerPlan.ActiveMode()).ConfigureAwait(true);
        return mode is { } m ? (true, m) : (false, default);
    }

    /// <summary>
    /// Seed the baseline from the SAME source the tick uses (the active Windows
    /// scheme), off the UI thread, so the first tick doesn't fire a spurious
    /// "changed on device" for a plan that was already active at load. Call before
    /// Start(). If the active scheme isn't one we map, leaves the baseline unset
    /// and the first tick establishes it silently.
    /// </summary>
    public async Task SeedBaselineAsync()
    {
        try
        {
            var mode = await Task.Run(() => WindowsPowerPlan.ActiveMode()).ConfigureAwait(true);
            if (mode is { } m) SetBaseline(m);
        }
        catch { /* leave unset; first tick seeds silently */ }
    }

    /// <summary>
    /// Suspend external-change detection while a local mode write is settling, so a
    /// tick mid-write can't yank the card selection. Mirrors FanStateMonitor.
    /// </summary>
    public void Suspend() => Suspended = true;

    /// <summary>Resume detection, re-seeding the baseline to the settled mode.</summary>
    public void Resume(PerformanceMode mode) { SetBaseline(mode); Suspended = false; }
}
