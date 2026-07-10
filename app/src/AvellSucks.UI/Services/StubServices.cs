using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Rgb;

namespace AvellSucks.UI.Services;

/// <summary>
/// Local, no-hardware implementations that keep in-memory state and report
/// EC/HID writes honestly as blocked when the write gate is closed. These let
/// the UI run and be validated anywhere; swap for Core-backed implementations
/// (WmiEcBackend / ITeRgbBackend) when wiring real hardware.
/// </summary>
/// <summary>
/// Single policy for whether the UI may WRITE to hardware. Writes are OFF by
/// default and opt-in — reads/telemetry work whenever the process is elevated,
/// but the reverse-engineered EC/PL writes (validated on one machine) stay
/// disabled until the user turns them on in Settings.
///
/// Resolution order:
///   • env GAMINGCENTER_ALLOW_EC_WRITES "0"/"false"/"no" → force OFF
///   • env GAMINGCENTER_ALLOW_EC_WRITES "1"/"true"       → force ON (Server parity,
///                                                          self-tests, dev opt-in)
///   • otherwise → the persisted user setting (AppSettings.EnableHardwareWrites),
///                 which defaults to false
/// Writes remain protected downstream by the allowlist + read-back verify +
/// rollback + audit, regardless of this gate. This is re-read live, so toggling
/// the Settings switch takes effect without a restart.
/// </summary>
internal static class WriteGateInfo
{
    /// <summary>The env override, if the var is set to a recognised value; else null.</summary>
    internal static bool? EnvOverride()
    {
        var raw = Environment.GetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES");
        if (raw is "0" or "false" or "FALSE" or "False" or "no" or "NO") return false;
        if (raw is "1" or "true" or "TRUE" or "True") return true;
        return null;
    }

    /// <summary>True when the env var forces the gate (user toggle is then locked).</summary>
    internal static bool IsEnvForced => EnvOverride() is not null;

    /// <summary>Live gate state: env override if set, else the persisted opt-in (default off).</summary>
    public static bool EcWritesEnabled =>
        EnvOverride() ?? Settings.SettingsStore.Current.Settings.EnableHardwareWrites;

    /// <summary>True when the process runs elevated (admin). Shared with HardwareServices.</summary>
    internal static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
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
    // Shared default presets (see PowerPresets); the real service uses the same
    // table as its fallback until it reads the silicon's actual defaults.
    private static readonly IReadOnlyDictionary<PerformanceMode, PowerLimits> Presets =
        PowerPresets.Default;

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

    public ValueTask<PowerLimits> GetPresetAsync(PerformanceMode mode, CancellationToken ct = default)
        => new(Presets[mode]);
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
