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

/// <summary>
/// The four performance modes the OEM Gaming Center exposed. Reverse-engineered
/// from the decompiled original (PowerPlan.cs: Gaming=1, High=2, Balance=3,
/// Saving=4). Each mode is a two-layer package: it activates a Windows power
/// scheme (powrprof) AND applies a CPU power-limit preset (PL1/PL2/PL4), the two
/// levers the original drove independently. Ordered hottest→coolest.
/// </summary>
public enum PerformanceMode { Gaming = 1, High = 2, Balanced = 3, Saving = 4 }

/// <summary>
/// Performance/energy control surface used by the Performance tab. Replaces the
/// earlier PL1/PL2/Tau-slider "Power" tab, which mixed two orthogonal systems
/// and used speculative EC addresses. A mode carries both a Windows power scheme
/// and a CPU power-limit preset; Advanced exposes the raw limits for tuning.
/// </summary>
public interface IPowerService
{
    /// <summary>Current mode + the CPU power limits in effect, or a degraded reading.</summary>
    ValueTask<PowerState> GetAsync(CancellationToken ct = default);

    /// <summary>Apply a mode: activate its Windows scheme and its PL preset.</summary>
    ValueTask<ControlResult> SetModeAsync(PerformanceMode mode, CancellationToken ct = default);

    /// <summary>Advanced: write raw CPU power limits (watts). Gated behind EC writes.</summary>
    ValueTask<ControlResult> SetLimitsAsync(PowerLimits limits, CancellationToken ct = default);

    /// <summary>Whether hardware writes are enabled (GAMINGCENTER_ALLOW_EC_WRITES).</summary>
    bool WritesEnabled { get; }
}

/// <summary>CPU power limits in watts (RAPL PL1 sustained, PL2 turbo, PL4 peak).</summary>
public sealed record PowerLimits(int Pl1Watts, int Pl2Watts, int Pl4Watts);

/// <summary>A snapshot of the active mode and the limits it resolved to.</summary>
public sealed record PowerState(PerformanceMode Mode, PowerLimits Limits, bool Supported, string? Error);

/// <summary>RGB keyboard control surface used by the RGB tab.</summary>
public interface IRgbService
{
    ValueTask<RgbState?> GetStateAsync(CancellationToken ct = default);
    ValueTask<ControlResult> ApplyAsync(RgbEffect effect, CancellationToken ct = default);
    bool Available { get; }
}
