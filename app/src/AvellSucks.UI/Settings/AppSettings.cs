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
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
