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
    private int _pl1 = 45, _pl2 = 60, _tau = 28;

    public bool WritesEnabled => WriteGateInfo.EcWritesEnabled;

    public ValueTask<PowerReading> GetAsync(CancellationToken ct = default)
        => new(new PowerReading(_pl1, _pl2, _tau, Supported: true, Error: null));

    public ValueTask<ControlResult> SetAsync(int pl1, int pl2, int tau, CancellationToken ct = default)
    {
        if (!WritesEnabled)
            return new(ControlResult.Blocked("EC writes disabled (GAMINGCENTER_ALLOW_EC_WRITES=0)"));
        _pl1 = pl1; _pl2 = pl2; _tau = tau;
        return new(ControlResult.Ok());
    }
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
