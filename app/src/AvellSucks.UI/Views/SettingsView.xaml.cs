using System.Windows;
using System.Windows.Controls;
using AvellSucks.Core.Service;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Services;
using AvellSucks.UI.Settings;
using AvellSucks.UI.Startup;

namespace AvellSucks.UI.Views;

/// <summary>
/// Preferences surface: interface language, start-with-Windows, start-minimized,
/// hide-to-tray-on-minimize, hardware writes, and the remote-access (network API +
/// MCP) controls. Every change applies live and persists (through
/// <see cref="SettingsStore"/> for app prefs, and the shared service config for
/// remote access). A load guard stops the initial control hydration (setting
/// IsChecked to reflect current state) from writing back.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = SettingsStore.Current.Settings;
    private readonly LoadingGate _loading = new(startActive: true);
    private readonly ServiceConfigManager _svcConfig = new();
    private readonly WindowsServiceControl _svc = new();
    private NetworkServiceConfig _remote = new();
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

        // Remote access: hydrate from the shared service config in %ProgramData%.
        // Reflect live service state (installed/running); the _loading gate stops
        // these programmatic sets from writing the config back.
        _remote = _svcConfig.Load();
        RunAsService.IsChecked = _svc.IsInstalled();
        ExposeNetwork.IsChecked = _remote.BindAddress != "127.0.0.1";
        ExposeDetails.Visibility = ExposeNetwork.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PopulateAddressPicker(_remote.BindAddress);
        PortBox.Text = _remote.Port.ToString();
        UseHttps.IsChecked = _remote.Scheme == "https";
        UseMtls.IsChecked = _remote.Auth.MtlsEnabled;
        RemoteWrites.IsChecked = _remote.AllowRemoteWrites;
        EnableMcp.IsChecked = _remote.McpEnabled;
        FirewallAutoOpen.IsChecked = _remote.FirewallAutoOpen;
        RefreshRemoteStatus();

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

    // ===== Remote access =====

    // Populate the bind-address picker: loopback, every IPv4 unicast address on
    // this machine (Tailscale/LAN), and 0.0.0.0 (all interfaces — documented as
    // unsafe on untrusted networks). Select the persisted address if present.
    private void PopulateAddressPicker(string current)
    {
        AddressPicker.Items.Clear();
        AddressPicker.Items.Add("127.0.0.1");
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(ua.Address))
                    AddressPicker.Items.Add(ua.Address.ToString());
        AddressPicker.Items.Add("0.0.0.0");
        AddressPicker.SelectedItem = AddressPicker.Items.Contains(current) ? current : "127.0.0.1";
    }

    // Drive the live status strip. State is carried by the dot color, the glyph AND
    // the text together (never hue alone) so it stays legible and honest about the
    // current exposure. Three states, escalating: stopped (neutral) → running on
    // loopback (ok, local only) → running + exposed off-loopback (danger tint, the
    // sharp edge). The listen URL is inlined here (rather than pulling in the whole
    // AvellSucks.Server project just for BuildListenUrl).
    private void RefreshRemoteStatus()
    {
        var running = _svc.IsRunning();
        var exposed = _remote.BindAddress != "127.0.0.1";
        var url = $"{_remote.Scheme}://{_remote.BindAddress}:{_remote.Port}";
        var where = exposed
            ? Loc.T("Settings.Remote.Status.Exposed")
            : Loc.T("Settings.Remote.Status.Loopback");

        RemoteStatusText.Text = running
            ? $"{string.Format(Loc.T("Settings.Remote.Status.Running"), url)} — {where}"
            : $"{Loc.T("Settings.Remote.Status.Stopped")} — {where}";

        // Segoe MDL2 Assets glyphs: E769 Pause (idle), E7BA Warning (exposed+live),
        // E72E Lock (live but local-only). Glyph + dot + text all carry the state,
        // never hue alone. Brushes resolve from the merged theme dictionaries.
        var glyph = !running ? "\uE769" : exposed ? "\uE7BA" : "\uE72E";
        var dotKey = !running ? "Ink3" : exposed ? "Danger" : "Ok";
        var fillKey = !running ? "Surface2" : exposed ? "DangerTint" : "OkTint";
        var borderKey = !running ? "Hairline" : exposed ? "Danger" : "Ok";

        RemoteStatusGlyph.Text = glyph;
        RemoteStatusGlyph.Foreground = Brush(dotKey);
        RemoteStatusDot.Fill = Brush(dotKey);
        RemoteStatusStrip.Background = Brush(fillKey);
        RemoteStatusStrip.BorderBrush = Brush(borderKey);
    }

    // Resolve a theme brush key from the merged application resources.
    private static System.Windows.Media.Brush Brush(string key)
        => (System.Windows.Media.Brush)Application.Current.Resources[key];

    private void OnRunAsService(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        if (RunAsService.IsChecked == true)
        {
            var exe = System.IO.Path.Combine(System.AppContext.BaseDirectory, "AvellSucks.Server.exe");
            _svc.Install(exe);
            _svc.Start();
        }
        else _svc.Uninstall();
        using (_loading.Begin()) RunAsService.IsChecked = _svc.IsInstalled();
        RefreshRemoteStatus();
    }

    private void OnExposeNetwork(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        bool expose = ExposeNetwork.IsChecked == true;
        ExposeDetails.Visibility = expose ? Visibility.Visible : Visibility.Collapsed;
        if (!expose) { _remote.BindAddress = "127.0.0.1"; using (_loading.Begin()) AddressPicker.SelectedItem = "127.0.0.1"; }
        else if (AddressPicker.SelectedItem is string a) _remote.BindAddress = a;
        _svcConfig.Save(_remote); RefreshRemoteStatus();
    }

    private void OnAddressChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading.Active) return;
        if (AddressPicker.SelectedItem is string a) { _remote.BindAddress = a; _svcConfig.Save(_remote); RefreshRemoteStatus(); }
    }

    private void OnPortChanged(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        if (int.TryParse(PortBox.Text, out var p) && p is > 0 and < 65536) { _remote.Port = p; _svcConfig.Save(_remote); RefreshRemoteStatus(); }
    }

    private void OnUseHttps(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        if (UseHttps.IsChecked == true)
        {
            _remote.Scheme = "https";
            var pfx = System.IO.Path.Combine(ServiceConfigPaths.Dir, "listener.pfx");
            using var cert = SelfSignedCertFactory.Create("avellsucks-local");
            SelfSignedCertFactory.ExportPfx(cert, pfx, null);
            _remote.HttpsCertPath = pfx;
        }
        else { _remote.Scheme = "http"; _remote.HttpsCertPath = null; }
        _svcConfig.Save(_remote); RefreshRemoteStatus();
    }

    private void OnUseMtls(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _remote.Auth.MtlsEnabled = UseMtls.IsChecked == true;
        _svcConfig.Save(_remote);
    }

    private void OnGenerateToken(object sender, RoutedEventArgs e)
    {
        var token = _svcConfig.GenerateAndStoreToken(_remote);
        _svcConfig.Save(_remote);
        try { System.Windows.Clipboard.SetText(token); } catch { /* clipboard busy */ }
        TokenNotice.Text = string.Format(Loc.T("Settings.Remote.TokenShownOnce"), token);
        TokenNotice.Visibility = Visibility.Visible;
    }

    private void OnRemoteWrites(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _remote.AllowRemoteWrites = RemoteWrites.IsChecked == true;
        _svcConfig.Save(_remote);
    }

    private void OnEnableMcp(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _remote.McpEnabled = EnableMcp.IsChecked == true;
        _svcConfig.Save(_remote);
    }

    private void OnFirewallAutoOpen(object sender, RoutedEventArgs e)
    {
        if (_loading.Active) return;
        _remote.FirewallAutoOpen = FirewallAutoOpen.IsChecked == true;
        _svcConfig.Save(_remote);
    }
}
