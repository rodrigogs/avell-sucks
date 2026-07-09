using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AvellSucks.UI.Localization;

namespace AvellSucks.UI.Settings;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON under
/// <c>%AppData%\AvellSucks\settings.json</c>, and applies the settings that
/// have a live side effect (UI culture, autostart registry). Single instance
/// via <see cref="Current"/>; the app reads/writes through it.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvellSucks");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        TypeInfoResolver = SettingsJsonContext.Default,
    };

    // The OS display language captured at process start, BEFORE Loc/ApplyLanguage
    // mutates CurrentUICulture. Resolving System later must read this snapshot, not
    // the (by then overwritten) live CurrentUICulture.
    private static readonly CultureInfo OsUiCulture = CultureInfo.CurrentUICulture;

    public static SettingsStore Current { get; } = Load();

    public AppSettings Settings { get; private set; }

    private SettingsStore(AppSettings settings) => Settings = settings;

    private static SettingsStore Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), typeof(AppSettings), JsonOpts) as AppSettings
                  ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable file: fall back to defaults rather than crash.
            settings = new AppSettings();
        }
        return new SettingsStore(settings);
    }

    /// <summary>Persist the current settings to disk (best-effort).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, typeof(AppSettings), JsonOpts));
        }
        catch (Exception ex)
        {
            // Disk locked / permissions — keep running with in-memory settings, but
            // leave a trace so a silently-not-persisting setting is diagnosable.
            App.Trace($"SettingsStore.Save failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve a <see cref="LanguagePreference"/> to the culture to apply.
    /// System → OS culture mapped (pt/pt-BR → pt-BR, everything else → en).
    /// </summary>
    public static CultureInfo ResolveCulture(LanguagePreference pref) => pref switch
    {
        LanguagePreference.Portuguese => new CultureInfo("pt-BR"),
        LanguagePreference.English => new CultureInfo("en"),
        _ => IsPortugueseSystem() ? new CultureInfo("pt-BR") : new CultureInfo("en"),
    };

    private static bool IsPortugueseSystem()
    {
        try
        {
            // Use the user's chosen Windows DISPLAY language captured at startup
            // (OsUiCulture), not InstalledUICulture (the MUI base the OS was installed
            // in, which a language-pack switch can't change) and not the live
            // CurrentUICulture (which ApplyLanguage has since overwritten). A PT user
            // on an EN-installed Avell should still default to PT. pt, pt-BR, pt-PT …
            // all → PT; else → EN.
            return OsUiCulture.TwoLetterISOLanguageName
                .Equals("pt", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Apply the persisted language to the running app (sets <see cref="Loc.Culture"/>).</summary>
    public void ApplyLanguage() => Loc.Instance.Culture = ResolveCulture(Settings.Language);
}
