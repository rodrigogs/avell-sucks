using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GamingCenter.Core.Rgb;

namespace GamingCenter.UI.Services;

/// <summary>
/// Local, no-hardware implementations that keep in-memory state and report
/// EC/HID writes honestly as blocked when the write gate is closed. These let
/// the UI run and be validated anywhere; swap for Core-backed implementations
/// (WmiEcBackend / ITeRgbBackend) when wiring real hardware.
/// </summary>
internal static class WriteGateInfo
{
    public static bool EcWritesEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES"), "1", StringComparison.Ordinal);
}

public sealed class LocalFanService : IFanService
{
    private string _mode = "auto";
    private List<FanPoint> _curve = new()
    {
        new(50, 40), new(60, 64), new(70, 90), new(80, 116), new(90, 140)
    };

    public bool WritesEnabled => WriteGateInfo.EcWritesEnabled;

    public ValueTask<string?> GetModeAsync(CancellationToken ct = default) => new(_mode);

    public ValueTask<ControlResult> SetModeAsync(string mode, CancellationToken ct = default)
    {
        if (!WritesEnabled)
            return new(ControlResult.Blocked("EC writes disabled (GAMINGCENTER_ALLOW_EC_WRITES=0)"));
        _mode = mode;
        return new(ControlResult.Ok());
    }

    public ValueTask<IReadOnlyList<FanPoint>> GetCurveAsync(CancellationToken ct = default)
        => new((IReadOnlyList<FanPoint>)_curve);

    public ValueTask<ControlResult> SetCurveAsync(IReadOnlyList<FanPoint> points, CancellationToken ct = default)
    {
        if (!WritesEnabled)
            return new(ControlResult.Blocked("EC writes disabled (GAMINGCENTER_ALLOW_EC_WRITES=0)"));
        _curve = new List<FanPoint>(points);
        _mode = "custom";
        return new(ControlResult.Ok());
    }
}

public sealed class LocalPowerService : IPowerService
{
    // Per-mode CPU power-limit presets (watts). Shape mirrors the OEM's
    // Gaming/Office defaults from the decompiled original; concrete values are
    // sane defaults for a 45 W-class mobile CPU until the backend reads the
    // silicon's real GetGamingPLDefaultValue()/GetOfficePLDefaultValue().
    private static readonly IReadOnlyDictionary<PerformanceMode, PowerLimits> Presets =
        new Dictionary<PerformanceMode, PowerLimits>
        {
            [PerformanceMode.Gaming]   = new(45, 90, 107),
            [PerformanceMode.High]     = new(35, 64, 90),
            [PerformanceMode.Balanced] = new(25, 45, 64),
            [PerformanceMode.Saving]   = new(15, 25, 35),
        };

    private PerformanceMode _mode = PerformanceMode.Balanced;
    private PowerLimits _limits = Presets[PerformanceMode.Balanced];

    public bool WritesEnabled => WriteGateInfo.EcWritesEnabled;

    public ValueTask<PowerState> GetAsync(CancellationToken ct = default)
        => new(new PowerState(_mode, _limits, Supported: true, Error: null));

    public ValueTask<ControlResult> SetModeAsync(PerformanceMode mode, CancellationToken ct = default)
    {
        if (!WritesEnabled)
            return new(ControlResult.Blocked("Hardware writes disabled (GAMINGCENTER_ALLOW_EC_WRITES=0)"));
        _mode = mode;
        _limits = Presets[mode];
        return new(ControlResult.Ok());
    }

    public ValueTask<ControlResult> SetLimitsAsync(PowerLimits limits, CancellationToken ct = default)
    {
        if (!WritesEnabled)
            return new(ControlResult.Blocked("Hardware writes disabled (GAMINGCENTER_ALLOW_EC_WRITES=0)"));
        _limits = limits;
        return new(ControlResult.Ok());
    }

    /// <summary>The preset limits for a mode (for UI preview before applying).</summary>
    public static PowerLimits PresetFor(PerformanceMode mode) => Presets[mode];
}

public sealed class LocalRgbService : IRgbService
{
    private RgbState _state = new(PowerOn: true, Brightness: 100, RgbEffectType.Static,
        RgbDirection.LeftToRight, RgbSpeed.Normal, LedCount: 0);

    // No HID device wired yet; report unavailable so the UI states it honestly.
    public bool Available => false;

    public ValueTask<RgbState?> GetStateAsync(CancellationToken ct = default) => new(_state);

    public ValueTask<ControlResult> ApplyAsync(RgbEffect effect, CancellationToken ct = default)
    {
        if (!Available)
            return new(ControlResult.Blocked("RGB HID device not connected"));
        _state = _state with
        {
            Effect = effect.Type,
            Brightness = effect.Brightness,
            Speed = effect.Speed,
            Direction = effect.Direction
        };
        return new(ControlResult.Ok());
    }
}
