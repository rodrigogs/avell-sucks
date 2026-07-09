using System;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;

namespace AvellSucks.UI.Services;

/// <summary>
/// Reconciler that keeps the UI's fan mode in sync with the DEVICE — reflecting
/// changes made OUTSIDE our app (the OEM Gaming Center, or the physical Fn fan
/// key). It re-reads the EC fan-control byte, maps it via <see cref="FanModeMap"/>,
/// and raises <see cref="ExternalModeChanged"/> (case-insensitive diff) when the
/// device changed on its own. See <see cref="StateReconciler{T}"/> for the shared
/// timer/baseline/diff machinery.
/// </summary>
public sealed class FanStateMonitor : StateReconciler<string>
{
    private readonly IEcBackend _backend;

    /// <summary>Raised (UI thread) when the device's fan mode changed externally.</summary>
    public event Action<string>? ExternalModeChanged;

    public FanStateMonitor(IEcBackend backend, double intervalSeconds = 2.5)
        : base(intervalSeconds, StringComparer.OrdinalIgnoreCase)
    {
        _backend = backend;
        ExternalChanged += m => ExternalModeChanged?.Invoke(m);
    }

    protected override async Task<(bool ok, string value)> ReadCurrentAsync()
    {
        var fanMode = await _backend.InterpretFanModeAsync().ConfigureAwait(true);
        return fanMode is null ? (false, "") : (true, FanModeMap.KeyFor(fanMode.RawValue));
    }

    /// <summary>
    /// Suspend external-change detection while a local write is settling. The EC
    /// reads the OLD value for up to ~2s during a mode transition (esp. Boost
    /// exit); without this the reconciler sees that stale value and yanks the UI
    /// selection back — the "goes and comes back" flicker. Balanced by Resume.
    /// </summary>
    public void Suspend() => Suspended = true;

    /// <summary>Resume detection, re-seeding the baseline to the settled value.</summary>
    public void Resume(string mode) { SetBaseline(mode); Suspended = false; }
}
