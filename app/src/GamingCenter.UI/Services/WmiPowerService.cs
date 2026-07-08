using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingCenter.Core.Hardware;
using GamingCenter.Core.Platforms;

namespace GamingCenter.UI.Services;

/// <summary>
/// Real, hardware-backed <see cref="IPowerService"/>. Reads/writes the CPU power
/// limits straight from the EC via <see cref="WmiEcBackend"/> + <see cref="SafeEcWriter"/>.
///
/// Addresses are CONFIRMED from the decompiled OEM GamingCenter:
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
    private const int ADDR_PL1 = 1923, ADDR_PL2 = 1924, ADDR_PL4 = 1925;
    private const int ADDR_GAMING_PL1 = 1840, ADDR_GAMING_PL2 = 1841, ADDR_GAMING_PL4 = 1842;
    private const int ADDR_OFFICE_PL1 = 1844, ADDR_OFFICE_PL2 = 1845, ADDR_OFFICE_PL4 = 1846;

    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;
    private readonly WriteGate _gate;

    public WmiPowerService(IEcBackend backend, SafeEcWriter writer, WriteGate gate)
    {
        _backend = backend;
        _writer = writer;
        _gate = gate;
    }

    public bool WritesEnabled => _gate.IsWriteAllowed;

    public async ValueTask<PowerState> GetAsync(CancellationToken ct = default)
    {
        var snap = await _backend.ReadSnapshotAsync([ADDR_PL1, ADDR_PL2, ADDR_PL4], ct).ConfigureAwait(false);
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
        // Two-layer package (matches the tab's promise): (1) activate the Windows
        // power plan, (2) apply the CPU PL preset to the EC. The plan switch is the
        // visible, verifiable lever; PL is best-effort on top.
        var (planOk, planErr) = await WindowsPowerPlan.SetAsync(mode, ct).ConfigureAwait(false);

        var preset = await GetPresetAsync(mode, ct).ConfigureAwait(false);
        var plResult = await WriteLimitsAsync(preset, $"ui:power/mode={mode}", ct).ConfigureAwait(false);

        // Success if the plan switched. PL may legitimately no-op (registers read
        // 0 / DC), so a PL failure alone does not fail the mode when the plan took.
        if (planOk)
            return plResult.Verified ? plResult : ControlResult.Ok();
        // Plan failed: surface it, but if PL still verified, report that truth.
        return plResult.Verified
            ? ControlResult.Ok()
            : ControlResult.Failed(planErr ?? plResult.Error ?? "Could not apply performance mode.");
    }

    public ValueTask<ControlResult> SetLimitsAsync(PowerLimits limits, CancellationToken ct = default)
        => WriteLimitsAsync(limits, "ui:power/limits", ct);

    private async ValueTask<ControlResult> WriteLimitsAsync(PowerLimits limits, string reason, CancellationToken ct)
    {
        // Write PL1, PL2, PL4 in order; any denial/failure aborts (SafeEcWriter
        // already rolled the offending register back). Byte-clamped for safety.
        var writes = new[]
        {
            (ADDR_PL1, Clamp(limits.Pl1Watts)),
            (ADDR_PL2, Clamp(limits.Pl2Watts)),
            (ADDR_PL4, Clamp(limits.Pl4Watts)),
        };
        EcWriteResult last = default!;
        foreach (var (addr, val) in writes)
        {
            last = await _writer.TryWriteAsync(addr, val, $"{reason}:0x{addr:X}={val}", ct).ConfigureAwait(false);
            if (!last.Allowed || !last.Verified)
                return new ControlResult(last.Allowed, last.Executed, last.Verified, last.Error);
        }
        return new ControlResult(last.Allowed, last.Executed, last.Verified, last.Error);
    }

    // Preset = the machine's own default PL registers for the nearest hardware
    // mode. On this machine those registers read 0 (the CPU power limits live in
    // Intel XTU / MSR, not the EC), so an all-zero read means "EC has no value" —
    // fall back to sane nominal presets rather than showing 0/0 W.
    public async ValueTask<PowerLimits> GetPresetAsync(PerformanceMode mode, CancellationToken ct = default)
    {
        var isGaming = mode is PerformanceMode.Gaming or PerformanceMode.High;
        int[] addrs = isGaming
            ? [ADDR_GAMING_PL1, ADDR_GAMING_PL2, ADDR_GAMING_PL4]
            : [ADDR_OFFICE_PL1, ADDR_OFFICE_PL2, ADDR_OFFICE_PL4];

        var snap = await _backend.ReadSnapshotAsync(addrs, ct).ConfigureAwait(false);
        var readOk = snap.Fields.All(f => f.Ok);
        var pl1 = readOk ? snap.Fields[0].Value : 0;
        var pl2 = readOk ? snap.Fields[1].Value : 0;
        var pl4 = readOk ? snap.Fields[2].Value : 0;

        // All-zero (or unreadable) → EC doesn't hold the limits on this board.
        if (!readOk || (pl1 == 0 && pl2 == 0 && pl4 == 0))
            return FallbackPreset(mode);

        return new PowerLimits(pl1, pl2, pl4);
    }

    private static PowerLimits FallbackPreset(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Gaming => new(45, 90, 107),
        PerformanceMode.High => new(35, 64, 90),
        PerformanceMode.Balanced => new(25, 45, 64),
        _ => new(15, 25, 35),
    };

    // Best-effort classification of the current limits back to a mode label.
    private static PerformanceMode InferMode(PowerLimits l) => l.Pl1Watts switch
    {
        >= 40 => PerformanceMode.Gaming,
        >= 30 => PerformanceMode.High,
        >= 20 => PerformanceMode.Balanced,
        _ => PerformanceMode.Saving,
    };

    private static int Clamp(int w) => Math.Clamp(w, 0, 254);
}
