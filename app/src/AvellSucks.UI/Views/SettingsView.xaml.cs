using System.Windows.Controls;
using AvellSucks.UI.Settings;
using AvellSucks.UI.Startup;

namespace AvellSucks.UI.Views;

/// <summary>
/// Preferences surface: interface language, start-with-Windows, start-minimized,
/// and hide-to-tray-on-minimize. Every change applies live and persists through
/// <see cref="SettingsStore"/>. A load guard stops the initial control hydration
/// (setting IsChecked to reflect current state) from writing back.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = SettingsStore.Current.Settings;
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();

        // Reflect persisted state without triggering the change handlers.
        switch (_settings.Language)
        {
            case LanguagePreference.English: LangEn.IsChecked = true; break;
            case LanguagePreference.Portuguese: LangPt.IsChecked = true; break;
            default: LangSystem.IsChecked = true; break;
        }

        // Autostart reflects the real registry state, not just the stored flag,
        // in case it was changed elsewhere (tray menu, another install).
        StartWithWindows.IsChecked = AutoStart.IsEnabled();
        StartMinimized.IsChecked = _settings.StartMinimized;
        HideOnMinimize.IsChecked = _settings.HideOnMinimize;

        _loading = false;

        // This view is cached and reused across tab switches, so the autostart
        // state can change elsewhere (the tray menu) after construction. Re-read
        // the real registry value each time the tab is shown so the toggle never
        // goes stale.
        Loaded += (_, _) =>
        {
            bool real = AutoStart.IsEnabled();
            if (StartWithWindows.IsChecked != real)
            {
                _loading = true;
                StartWithWindows.IsChecked = real;
                _loading = false;
            }
        };
    }

    private void OnLanguageChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Language =
            LangEn.IsChecked == true ? LanguagePreference.English :
            LangPt.IsChecked == true ? LanguagePreference.Portuguese :
            LanguagePreference.System;
        SettingsStore.Current.ApplyLanguage();
        SettingsStore.Current.Save();
    }

    private void OnStartWithWindows(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading) return;
        bool enable = StartWithWindows.IsChecked == true;
        AutoStart.Set(enable);
        // Reflect reality in case the registry write was denied.
        bool real = AutoStart.IsEnabled();
        if (real != enable) { _loading = true; StartWithWindows.IsChecked = real; _loading = false; }
        _settings.StartWithWindows = real;
        SettingsStore.Current.Save();
    }

    private void OnStartMinimized(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StartMinimized = StartMinimized.IsChecked == true;
        SettingsStore.Current.Save();
    }

    private void OnHideOnMinimize(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.HideOnMinimize = HideOnMinimize.IsChecked == true;
        SettingsStore.Current.Save();
    }
}
