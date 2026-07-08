using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingCenter.Core.Hardware;
using GamingCenter.Core.Platforms;

namespace GamingCenter.UI.Services;

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
    // ADDR_MAFAN_CONTROL_BYTE (0x751). Mode byte values match FanController /
    // the decompiled MyFanCTLByteFlag.
    private const int MAFanControl = 1873;
    private const int CustomFanMode = 160; // 0xA0 advanced-custom
    private const int MaxPwm = 0x8C;       // 140

    // PWM source bytes for the five custom levels (0x743..0x747).
    private static readonly int[] s_curveAddresses = [1859, 1860, 1861, 1862, 1863];
    private static readonly int[] s_defaultCurveTemps = [50, 60, 70, 80, 90];

    // Mode key → control byte (mirrors FanController.s_modes).
    private static readonly Dictionary<string, int> s_modeToByte = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auto"] = 0, ["boost"] = 64, ["custom"] = 160,
        ["L1"] = 129, ["L2"] = 130, ["L3"] = 131, ["L4"] = 132, ["L5"] = 133,
    };

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
        // Map the raw control byte back to a UI mode key.
        return mode.RawValue switch
        {
            0 => "auto",
            64 => "boost",
            160 => "custom",
            129 => "L1", 130 => "L2", 131 => "L3", 132 => "L4", 133 => "L5",
            _ => "auto",
        };
    }

    public async ValueTask<ControlResult> SetModeAsync(string mode, CancellationToken ct = default)
    {
        if (!s_modeToByte.TryGetValue(mode, out var value))
            return ControlResult.Failed($"Unknown fan mode '{mode}'.");

        var r = await _writer.TryWriteAsync(MAFanControl, value, $"ui:fan/mode={mode}", ct)
            .ConfigureAwait(false);
        return ToControlResult(r);
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
            if (!r.Allowed || !r.Verified) return ToControlResult(r);
        }

        var mode = await _writer.TryWriteAsync(MAFanControl, CustomFanMode, "ui:fan/curve:enable-custom", ct)
            .ConfigureAwait(false);
        return ToControlResult(mode);
    }

    // EcWriteResult (Allowed/Executed/Verified/Error) → ControlResult, 1:1.
    private static ControlResult ToControlResult(EcWriteResult r) =>
        new(r.Allowed, r.Executed, r.Verified, r.Error);
}
