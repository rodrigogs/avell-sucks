using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;

namespace AvellSucks.UI.Services;

/// <summary>
/// Real, hardware-backed <see cref="IFanService"/>. Reads the fan-control byte
/// and PWM levels straight from the EC via <see cref="WmiEcBackend"/>, and writes
/// through <see cref="SafeEcWriter"/> (gate + allowlist + read-back verify +
/// rollback + audit). Drop-in for the stub <see cref="LocalFanService"/> — same
/// interface, same ControlResult/WriteState contract — so the UI is unchanged.
///
/// Addresses are the confirmed fan surface (RE + ec-read-probe.json):
///   0x751 (1873) ADDR_MAFAN_CONTROL_BYTE — mode
///   0x743..0x747 (1859..1863) custom PWM levels L1..L5
/// Power limits are NOT handled here (their EC encoding is still speculative);
/// the Performance tab stays on the stub until validated.
/// </summary>
public sealed class WmiFanService : IFanService
{
    // Fan control surface — all constants come from the shared FanModeMap so this
    // service, FanController, the reconciler and the allowlist never disagree.
    private const int MAFanControl = FanModeMap.ControlByteAddress;
    private const int CustomFanMode = FanModeMap.CustomModeByte;
    private const int MaxPwm = FanModeMap.MaxPwm;
    private static readonly int[] s_curveAddresses = FanModeMap.CurveAddresses;
    private static readonly int[] s_defaultCurveTemps = FanModeMap.DefaultCurveTemps;

    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;
    private readonly WriteGate _gate;

    public WmiFanService(IEcBackend backend, SafeEcWriter writer, WriteGate gate)
    {
        _backend = backend;
        _writer = writer;
        _gate = gate;
    }

    public bool WritesEnabled => _gate.IsWriteAllowed;

    public async ValueTask<string?> GetModeAsync(CancellationToken ct = default)
    {
        var mode = await _backend.InterpretFanModeAsync(ct).ConfigureAwait(false);
        if (mode is null) return null;
        return FanModeMap.KeyFor(mode.RawValue);
    }

    public async ValueTask<ControlResult> SetModeAsync(string mode, CancellationToken ct = default)
    {
        if (!FanModeMap.TryByteFor(mode, out var value))
            return ControlResult.Failed($"Unknown fan mode '{mode}'.");

        var r = await _writer.TryWriteAsync(MAFanControl, value, $"ui:fan/mode={mode}", ct)
            .ConfigureAwait(false);
        return ControlResult.From(r);
    }

    public async ValueTask<IReadOnlyList<FanPoint>> GetCurveAsync(CancellationToken ct = default)
    {
        var snapshot = await _backend.ReadSnapshotAsync(s_curveAddresses, ct).ConfigureAwait(false);
        var points = new List<FanPoint>(s_curveAddresses.Length);
        for (var i = 0; i < s_curveAddresses.Length; i++)
        {
            var field = snapshot.Fields.FirstOrDefault(f => f.Address == s_curveAddresses[i]);
            var pwm = field is { Ok: true } ? field.Value : 0;
            points.Add(new FanPoint(s_defaultCurveTemps[i], pwm));
        }
        return points;
    }

    public async ValueTask<ControlResult> SetCurveAsync(IReadOnlyList<FanPoint> points, CancellationToken ct = default)
    {
        if (points is null || points.Count != s_curveAddresses.Length)
            return ControlResult.Failed($"Fan curve needs exactly {s_curveAddresses.Length} points.");

        // Write each PWM level, then flip the mode to custom. Any denial/failure
        // aborts and surfaces through the ControlResult (SafeEcWriter already
        // rolled back the offending register).
        for (var i = 0; i < s_curveAddresses.Length; i++)
        {
            var pwm = Math.Clamp(points[i].Pwm, 0, MaxPwm);
            var r = await _writer.TryWriteAsync(
                s_curveAddresses[i], pwm,
                $"ui:fan/curve:L{i + 1}={points[i].TemperatureC}C:{pwm}", ct)
                .ConfigureAwait(false);
            if (!r.Allowed || !r.Verified) return ControlResult.From(r);
        }

        var mode = await _writer.TryWriteAsync(MAFanControl, CustomFanMode, "ui:fan/curve:enable-custom", ct)
            .ConfigureAwait(false);
        return ControlResult.From(mode);
    }
}
