using System.Windows.Controls;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Services;
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
    private readonly LoadingGate _loading = new(startActive: true);
    // Remembers the last update-status message as a (key, arg) pair so it can be
    // re-localized when the language changes (the label's {loc:Tr} binding is
    // replaced by a literal once we set .Text, so we recompute it ourselves).
    private (string key, string? arg)? _lastUpdateStatus;

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

        // Hardware writes: reflect the live gate. When the env var forces the
        // state, the persisted toggle can't change anything, so lock it and say so.
        EnableWrites.IsChecked = WriteGateInfo.EcWritesEnabled;
        if (WriteGateInfo.IsEnvForced)
        {
            EnableWrites.IsEnabled = false;
            WritesEnvNotice.Visibility = System.Windows.Visibility.Visible;
        }

        // The version line and the update-status line are set imperatively (their
        // {loc:Tr} bindings get replaced by literals), so re-localize them now and
        // on every runtime language change.
        Loc.OnCultureChanged(RefreshUpdateTexts);

        _loading.End();

        // This view is cached and reused across tab switches, so the autostart
        // state can change elsewhere (the tray menu) after construction. Re-read
        // the real registry value each time the tab is shown so the toggle never
        // goes stale.
        Loaded += (_, _) =>
        {
            bool real = AutoStart.IsEnabled();
            if (StartWithWindows.IsChecked != real)
                using (_loading.Begin())
                    StartWithWindows.IsChecked = real;
        };
    }

    private void OnLanguageChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _settings.Language =
            LangEn.IsChecked == true ? LanguagePreference.English :
            LangPt.IsChecked == true ? LanguagePreference.Portuguese :
            LanguagePreference.System;
        SettingsStore.Current.ApplyLanguage();
        SettingsStore.Current.Save();
    }

    private void OnStartWithWindows(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading.Active) return;
        bool enable = StartWithWindows.IsChecked == true;
        AutoStart.Set(enable);
        // Reflect reality in case the registry write was denied.
        bool real = AutoStart.IsEnabled();
        if (real != enable) using (_loading.Begin()) StartWithWindows.IsChecked = real;
        _settings.StartWithWindows = real;
        SettingsStore.Current.Save();
    }

    private void OnStartMinimized(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _settings.StartMinimized = StartMinimized.IsChecked == true;
        SettingsStore.Current.Save();
    }

    private void OnHideOnMinimize(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _settings.HideOnMinimize = HideOnMinimize.IsChecked == true;
        SettingsStore.Current.Save();
    }

    // Opt in/out of hardware writes. Persisted and read live by the write gate, so
    // it takes effect immediately (a Fan/Power apply switches between actuating and
    // showing the "writes disabled" notice without a restart). Env-forced state is
    // handled by locking the toggle in the ctor, so this only runs on user intent.
    private void OnEnableWrites(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _settings.EnableHardwareWrites = EnableWrites.IsChecked == true;
        SettingsStore.Current.Save();
    }

    // Re-localize the version line and the last update-status message. Called on
    // construction and on any runtime language change.
    private void RefreshUpdateTexts()
    {
        VersionText.Text = string.Format(Loc.T("Settings.Version"), Updater.CurrentVersion().ToString(3));
        if (_lastUpdateStatus is { } s)
            UpdateStatusText.Text = s.arg is null ? Loc.T(s.key) : string.Format(Loc.T(s.key), s.arg);
    }

    // Set the update-status line and remember it (key + optional arg) so a later
    // language switch can re-localize it.
    private void SetUpdateStatus(string key, string? arg = null)
    {
        _lastUpdateStatus = (key, arg);
        UpdateStatusText.Text = arg is null ? Loc.T(key) : string.Format(Loc.T(key), arg);
    }

    // Manual "check for updates". Queries GitHub; if newer, downloads the installer
    // and relaunches it silently (the app exits so the installer can overwrite).
    private async void OnCheckUpdates(object sender, System.Windows.RoutedEventArgs e)
    {
        CheckUpdatesBtn.IsEnabled = false;
        SetUpdateStatus("Settings.Updates.Checking");
        try
        {
            var check = await Updater.CheckAsync();
            switch (check.Status)
            {
                case UpdateStatus.UpToDate:
                    SetUpdateStatus("Settings.Updates.UpToDate");
                    break;
                case UpdateStatus.UpdateAvailable:
                    SetUpdateStatus("Settings.Updates.Downloading", check.LatestVersion);
                    if (check.AssetUrl is not null)
                    {
                        // Applies and shuts the app down; if it returns, it failed.
                        var ok = await Updater.DownloadAndApplyAsync(check, () =>
                            System.Windows.Application.Current.Shutdown());
                        if (!ok) SetUpdateStatus("Settings.Updates.Failed");
                    }
                    else
                    {
                        // Release exists but no installer asset — point at the page.
                        SetUpdateStatus("Settings.Updates.Available", check.LatestVersion);
                        Updater.OpenReleasesPage();
                    }
                    break;
                case UpdateStatus.NoConnection:
                    SetUpdateStatus("Settings.Updates.NoConnection");
                    break;
                default:
                    SetUpdateStatus("Settings.Updates.Failed");
                    break;
            }
        }
        finally
        {
            CheckUpdatesBtn.IsEnabled = true;
        }
    }
}
