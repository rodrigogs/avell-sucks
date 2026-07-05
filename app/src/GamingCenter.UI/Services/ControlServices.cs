using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GamingCenter.Core.Rgb;

namespace GamingCenter.UI.Services;

/// <summary>
/// Normalized result of a hardware mutation, mirroring the API's
/// allowed/executed/verified contract so the UI can be honest about writes.
/// </summary>
public sealed record ControlResult(bool Allowed, bool Executed, bool Verified, string? Error)
{
    public static ControlResult Ok() => new(true, true, true, null);
    public static ControlResult Blocked(string why) => new(false, false, false, why);
    public static ControlResult Failed(string why) => new(true, true, false, why);

    /// <summary>Overall state for badge rendering.</summary>
    public WriteState State =>
        !Allowed ? WriteState.Blocked
        : !Verified ? WriteState.Failed
        : WriteState.Verified;
}

public enum WriteState { Idle, Pending, Verified, Failed, Blocked }

/// <summary>Fan control surface used by the Fan tab.</summary>
public interface IFanService
{
    /// <summary>Current interpreted mode key (auto/boost/custom/L1..L5), or null if unreadable.</summary>
    ValueTask<string?> GetModeAsync(CancellationToken ct = default);

    ValueTask<ControlResult> SetModeAsync(string mode, CancellationToken ct = default);

    /// <summary>Five temp→PWM points (PWM 0..140).</summary>
    ValueTask<IReadOnlyList<FanPoint>> GetCurveAsync(CancellationToken ct = default);

    ValueTask<ControlResult> SetCurveAsync(IReadOnlyList<FanPoint> points, CancellationToken ct = default);

    /// <summary>Whether EC writes are enabled (GAMINGCENTER_ALLOW_EC_WRITES).</summary>
    bool WritesEnabled { get; }
}

public sealed record FanPoint(int TemperatureC, int Pwm);

/// <summary>Power-limit control surface used by the Power tab.</summary>
public interface IPowerService
{
    ValueTask<PowerReading> GetAsync(CancellationToken ct = default);
    ValueTask<ControlResult> SetAsync(int pl1Watts, int pl2Watts, int tauSeconds, CancellationToken ct = default);
    bool WritesEnabled { get; }
}

public sealed record PowerReading(int Pl1Watts, int Pl2Watts, int TauSeconds, bool Supported, string? Error);

/// <summary>RGB keyboard control surface used by the RGB tab.</summary>
public interface IRgbService
{
    ValueTask<RgbState?> GetStateAsync(CancellationToken ct = default);
    ValueTask<ControlResult> ApplyAsync(RgbEffect effect, CancellationToken ct = default);
    bool Available { get; }
}
