using System.Collections.Generic;
using System.Text.Json.Serialization;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Settings;

/// <summary>Language preference. <see cref="System"/> follows the OS culture.</summary>
public enum LanguagePreference
{
    System,
    English,
    Portuguese,
}

/// <summary>
/// User preferences persisted to disk (see <see cref="SettingsStore"/>).
/// Plain data — no behavior — so it serializes cleanly to JSON.
/// </summary>
public sealed class AppSettings
{
    /// <summary>UI language. Default follows the system culture (pt/pt-BR → PT, else EN).</summary>
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    /// <summary>Launch the app when the user signs in to Windows (elevated Scheduled Task; see AutoStart).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Minimize sends the window to the tray instead of the taskbar.</summary>
    public bool HideOnMinimize { get; set; } = true;

    /// <summary>Start minimized to the tray (useful together with StartWithWindows).</summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Allow the app to WRITE to hardware (EC registers + CPU power limits).
    /// On by default: this is a control center for the machine it was built for,
    /// so the fan/power controls actuate out of the box. Turn it off in
    /// Settings → Hardware writes for a read-only preview. The
    /// <c>GAMINGCENTER_ALLOW_EC_WRITES</c> env var still force-overrides this
    /// (<c>0</c> forces off regardless of the saved setting).
    /// </summary>
    public bool EnableHardwareWrites { get; set; } = true;

    /// <summary>
    /// The last Fan + Power profile the user successfully applied, captured so it
    /// can be re-actuated on startup (the EC forgets these across a reboot; the app
    /// only reads the EC at launch and never reapplied). Null on a fresh install —
    /// nothing to restore. See <see cref="Services.ProfileRestorer"/>.
    /// </summary>
    public RestoreProfile? RestoreProfile { get; set; }
}

/// <summary>
/// The persisted last-applied hardware profile (fan mode/curve + power mode),
/// re-actuated on startup by <see cref="Services.ProfileRestorer"/>. Plain data —
/// serialized to JSON via <see cref="SettingsJsonContext"/>.
/// </summary>
public sealed class RestoreProfile
{
    /// <summary>Fan mode key (auto/boost/custom/l1..l5). "custom" when a curve is set.</summary>
    public string? FanMode { get; set; }

    /// <summary>Custom fan curve (five temp→PWM points), set when the user applied a curve.</summary>
    public List<FanCurvePoint>? FanCurve { get; set; }

    /// <summary>Last-applied performance mode.</summary>
    public PerformanceMode? PowerMode { get; set; }
}

/// <summary>A single temp→PWM point of a persisted fan curve (mirrors <see cref="Services.FanPoint"/>).</summary>
public sealed class FanCurvePoint
{
    public int TemperatureC { get; set; }
    public int Pwm { get; set; }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RestoreProfile))]
[JsonSerializable(typeof(FanCurvePoint))]
[JsonSerializable(typeof(List<FanCurvePoint>))]
[JsonSerializable(typeof(PerformanceMode))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
