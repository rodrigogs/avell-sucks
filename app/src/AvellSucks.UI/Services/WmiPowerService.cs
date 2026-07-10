using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;

namespace AvellSucks.UI.Services;

/// <summary>
/// Real, hardware-backed <see cref="IPowerService"/>. Reads/writes the CPU power
/// limits straight from the EC via <see cref="WmiEcBackend"/> + <see cref="SafeEcWriter"/>.
///
/// Addresses are CONFIRMED from the decompiled OEM AvellSucks:
///   0x783/0x784/0x785 (1923/1924/1925) — PL1/PL2/PL4 setting bytes (watts)
///   0x730/0x731/0x732 (1840/1841/1842) — Gaming-mode PL defaults (read-only)
///   0x734/0x735/0x736 (1844/1845/1846) — Office-mode PL defaults (read-only)
///
/// Mode → PL preset uses the machine's OWN default registers (like the OEM's
/// GetGamingPLDefaultValue / GetOfficePLDefaultValue) rather than hardcoded
/// watts, so the presets match this silicon. The four UI modes collapse onto the
/// two hardware defaults the OEM exposes (Gaming, Office) with scaled midpoints.
/// Tau is out of scope (the OEM sets it via Intel XTU, not the EC).
/// </summary>
public sealed class WmiPowerService : IPowerService
{
    // All PL addresses come from the shared PowerRegisters source (see FanModeMap
    // for the same pattern on the fan surface).
    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;
    private readonly WriteGate _gate;

    // Preset defaults come from fixed EC registers (or the nominal fallback when
    // they read 0). They don't change unless we write PL, so cache per mode —
    // BuildModeCards + OnLoaded + OnModeChecked otherwise re-read the same
    // registers a dozen+ times just to open the Performance tab.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PerformanceMode, PowerLimits> _presetCache = new();

    public WmiPowerService(IEcBackend backend, SafeEcWriter writer, WriteGate gate)
    {
        _backend = backend;
        _writer = writer;
        _gate = gate;
    }

    public bool WritesEnabled => _gate.IsWriteAllowed;

    public async ValueTask<PowerState> GetAsync(CancellationToken ct = default)
    {
        var snap = await _backend.ReadSnapshotAsync(PowerRegisters.Setting, ct).ConfigureAwait(false);
        var readOk = snap.Fields.All(f => f.Ok);
        var live = readOk
            ? new PowerLimits(snap.Fields[0].Value, snap.Fields[1].Value, snap.Fields[2].Value)
            : new PowerLimits(0, 0, 0);

        // Mode reflects the ACTIVE WINDOWS POWER PLAN — the authoritative, always-
        // readable source — not the EC PL bytes (which read 0 on this machine and
        // gave a bogus "always Saving"). powercfg spawns subprocesses, so run it
        // OFF the UI thread (this may be called from a UI continuation).
        var mode = await Task.Run(() => WindowsPowerPlan.ActiveMode()).ConfigureAwait(false)
                   ?? InferMode(live);

        // When the EC doesn't hold the limits (all-zero on this board), show the
        // mode's nominal preset as the envelope instead of a bogus 0/0/0.
        var limits = (live.Pl1Watts == 0 && live.Pl2Watts == 0 && live.Pl4Watts == 0)
            ? await GetPresetAsync(mode, ct).ConfigureAwait(false)
            : live;

        return new PowerState(mode, limits, Supported: true, Error: null);
    }

    public async ValueTask<ControlResult> SetModeAsync(PerformanceMode mode, CancellationToken ct = default)
    {
        // Selecting a performance mode actuates the machine (it switches the Windows
        // power plan AND writes the PL preset), so it must honor the same write gate
        // as every EC write — otherwise the read/write decouple would let a mode
        // switch mutate system power state while writes are "off". Deny up front,
        // consistent with the fan path and the stub service.
        if (!_gate.IsWriteAllowed)
            return ControlResult.Blocked("Hardware writes are disabled. Enable them in Settings to change the performance mode.");

        // The mode's PRIMARY, verifiable lever is the Windows power plan — that's
        // what actually switches on this machine (read back via powercfg). The CPU
        // PL bytes are a best-effort second layer: on this board they live in Intel
        // XTU/MSR (EC reads 0), so a PL write may not latch. We therefore anchor
        // the reported outcome on the plan switch and NEVER claim success when it
        // fails. PL is attempted quietly; its failure does not fake a green toast.
        var (planOk, planErr) = await WindowsPowerPlan.SetAsync(mode, ct).ConfigureAwait(false);
        if (!planOk)
            return ControlResult.Failed(planErr ?? "Could not switch the Windows power plan.");

        // Best-effort PL preset write (audited); result intentionally not surfaced
        // as the mode outcome — see above. Advanced sliders (SetLimitsAsync) report
        // the PL truth directly, because there the user asked for PL specifically.
        var preset = await GetPresetAsync(mode, ct).ConfigureAwait(false);
        _ = await WriteLimitsAsync(preset, $"ui:power/mode={mode}", ct).ConfigureAwait(false);

        return ControlResult.Ok();
    }

    /// <summary>
    /// Advanced tuning: write raw CPU power limits. Reports the PL write truth
    /// honestly — if the EC doesn't latch the value (limits managed by Intel XTU
    /// on this board), the read-back verify fails and this surfaces as blocked,
    /// not a fake success.
    /// </summary>
    public ValueTask<ControlResult> SetLimitsAsync(PowerLimits limits, CancellationToken ct = default)
        => WriteLimitsAsync(limits, "ui:power/limits", ct);

    private async ValueTask<ControlResult> WriteLimitsAsync(PowerLimits limits, string reason, CancellationToken ct)
    {
        // Write PL1, PL2, PL4 in order; any denial/failure aborts (SafeEcWriter
        // already rolled the offending register back). Byte-clamped for safety.
        var writes = new[]
        {
            (PowerRegisters.Pl1, Clamp(limits.Pl1Watts)),
            (PowerRegisters.Pl2, Clamp(limits.Pl2Watts)),
            (PowerRegisters.Pl4, Clamp(limits.Pl4Watts)),
        };
        EcWriteResult last = default!;
        foreach (var (addr, val) in writes)
        {
            last = await _writer.TryWriteAsync(addr, val, $"{reason}:0x{addr:X}={val}", ct).ConfigureAwait(false);
            if (!last.Allowed || !last.Verified)
                return ControlResult.From(last);
        }
        _presetCache.Clear(); // limits changed on the silicon — presets may have shifted
        return ControlResult.From(last);
    }

    // Preset = the machine's own default PL registers for the nearest hardware
    // mode. On this machine those registers read 0 (the CPU power limits live in
    // Intel XTU / MSR, not the EC), so an all-zero read means "EC has no value" —
    // fall back to sane nominal presets rather than showing 0/0 W.
    public async ValueTask<PowerLimits> GetPresetAsync(PerformanceMode mode, CancellationToken ct = default)
    {
        if (_presetCache.TryGetValue(mode, out var cached)) return cached;

        var isGaming = mode is PerformanceMode.Gaming or PerformanceMode.High;
        int[] addrs = isGaming ? PowerRegisters.GamingDefaults : PowerRegisters.OfficeDefaults;

        var snap = await _backend.ReadSnapshotAsync(addrs, ct).ConfigureAwait(false);
        var readOk = snap.Fields.All(f => f.Ok);
        var pl1 = readOk ? snap.Fields[0].Value : 0;
        var pl2 = readOk ? snap.Fields[1].Value : 0;
        var pl4 = readOk ? snap.Fields[2].Value : 0;

        // All-zero (or unreadable) → EC doesn't hold the limits on this board.
        var preset = (!readOk || (pl1 == 0 && pl2 == 0 && pl4 == 0))
            ? FallbackPreset(mode)
            : new PowerLimits(pl1, pl2, pl4);
        _presetCache[mode] = preset;
        return preset;
    }

    private static PowerLimits FallbackPreset(PerformanceMode mode) => PowerPresets.For(mode);

    // Best-effort classification of the current limits back to a mode label.
    private static PerformanceMode InferMode(PowerLimits l) => l.Pl1Watts switch
    {
        >= 40 => PerformanceMode.Gaming,
        >= 30 => PerformanceMode.High,
        >= 20 => PerformanceMode.Balanced,
        _ => PerformanceMode.Saving,
    };

    private static int Clamp(int w) => Math.Clamp(w, 0, PowerRegisters.MaxWatts);
}
