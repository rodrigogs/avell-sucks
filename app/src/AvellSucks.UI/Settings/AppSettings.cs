using System.Text.Json.Serialization;

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

    /// <summary>Launch the app when the user signs in to Windows (HKCU Run key).</summary>
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
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
