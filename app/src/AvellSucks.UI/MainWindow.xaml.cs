using System;
using System.Windows;
using System.Windows.Controls;
using AvellSucks.UI.Hardware;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Settings;
using AvellSucks.UI.Startup;
using AvellSucks.UI.Views;

namespace AvellSucks.UI;

public partial class MainWindow : Window
{
    private bool _reallyClosing;
    private readonly SensorPump _pump = new();
    private readonly DashboardView _dashboard;
    // Resource keys for the active tab's top-bar title/subtitle, so a live
    // language change can re-localize the header for whatever tab is showing.
    private string _titleKey = "Nav.Dashboard";
    private string _subKey = "Tab.Dashboard.Sub";

    // Fan/RGB/Performance are built on FIRST navigation, not at startup. Only the
    // Dashboard is shown initially; eagerly constructing the other three parsed
    // their XAML and spun up their monitors + EC reads (PowerView.BuildModeCards
    // fires 4 preset reads; FanView/PowerView create polling monitors) before the
    // window even appeared. Lazy construction keeps first-open cheap.
    private FanView? _fan;
    private DevicesView? _devices;
    private RgbView? _rgb;
    private PowerView? _power;
    private AboutView? _about;
    private SettingsView? _settings;
    private FanView Fan => _fan ??= new FanView(_pump);
    private DevicesView Devices => _devices ??= new DevicesView();
    private RgbView Rgb => _rgb ??= new RgbView();
    private PowerView Power => _power ??= new PowerView();
    private AboutView About => _about ??= new AboutView();
    private SettingsView SettingsTab => _settings ??= new SettingsView();

    public MainWindow()
    {
        InitializeComponent();

        // One sensor pump, shared by the views that need telemetry (Dashboard,
        // Fan). It opens its ring-0 monitor off-thread on the first Start().
        _dashboard = new DashboardView(_pump);

        // Keep the on-screen placement fix: CenterScreen produced Top/Left=NaN
        // under the RDP session, parking the window off-screen.
        Loaded += OnLoaded;
        TabHost.Content = _dashboard;
        // The top-bar title/subtitle are set imperatively (not {loc:Tr} bindings),
        // so re-localize them now and on every runtime language change.
        Loc.OnCultureChanged(RefreshHeader);

        // Optional deep-link to a tab for screenshot validation (GC_START_TAB=fan|rgb|power).
        var startTab = Environment.GetEnvironmentVariable("GC_START_TAB")?.Trim().ToLowerInvariant();
        RadioButton? initial = startTab switch
        {
            "fan" => NavFan,
            "devices" => NavDevices,
            "rgb" => NavRgb,
            "power" => NavPower,
            "about" => NavAbout,
            "settings" => NavSettings,
            _ => null,
        };
        if (initial is not null) initial.IsChecked = true;

        AutoStartItem.IsChecked = AutoStart.IsEnabled();
    }

    // ===== System tray =====

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Minimize hides to tray instead of the taskbar — unless the user opted
        // out in Settings, in which case it minimizes to the taskbar as usual.
        if (WindowState == WindowState.Minimized && SettingsStore.Current.Settings.HideOnMinimize)
        {
            Hide();
        }
    }

    /// <summary>
    /// Bring the app up hidden in the tray at startup. WPF needs the window shown
    /// once to realize its visual tree (which is what instantiates the tray icon
    /// and runs OnLoaded); we keep it off the taskbar across that Show()/Hide() so
    /// nothing flashes. Called only when StartMinimized + HideOnMinimize are set.
    /// </summary>
    public void StartHiddenInTray()
    {
        ShowInTaskbar = false;
        Show();
        Hide();
        ShowInTaskbar = true; // future restores appear on the taskbar normally
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e) => RestoreFromTray();
    private void OnTrayOpen(object sender, RoutedEventArgs e) => RestoreFromTray();

    // Re-read the real autostart state each time the tray menu opens, so it never
    // shows stale if the Settings tab changed it (or vice versa).
    private void OnTrayMenuOpened(object sender, RoutedEventArgs e)
        => AutoStartItem.IsChecked = AutoStart.IsEnabled();

    private void OnToggleAutoStart(object sender, RoutedEventArgs e)
    {
        bool enable = AutoStartItem.IsChecked;
        AutoStart.Set(enable);
        // Reflect the real state in case the registry write failed, and keep the
        // persisted setting (and the Settings tab, next time it opens) in sync.
        bool real = AutoStart.IsEnabled();
        AutoStartItem.IsChecked = real;
        SettingsStore.Current.Settings.StartWithWindows = real;
        SettingsStore.Current.Save();
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        _reallyClosing = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window close button minimizes to tray; real exit is the tray menu.
        if (!_reallyClosing)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        Tray.Dispose();
        _pump.Dispose();
        base.OnClosing(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            double left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            double top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
            if (double.IsNaN(left) || double.IsInfinity(left)) left = wa.Left + 40;
            if (double.IsNaN(top) || double.IsInfinity(top)) top = wa.Top + 40;
            Left = left;
            Top = top;
            // Don't force the window open when the user asked to start minimized
            // (to the tray): centering is fine, but un-minimizing/activating isn't.
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Topmost = true;
                Activate();
                Topmost = false;
            }
        }
        catch
        {
            Left = 80;
            Top = 80;
        }
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (TabHost is null || sender is not RadioButton rb) return;

        (UserControl view, string titleKey, string subKey) target = rb.Name switch
        {
            nameof(NavFan) => (Fan, "Nav.Fan", "Tab.Fan.Sub"),
            nameof(NavDevices) => (Devices, "Nav.Devices", "Tab.Devices.Sub"),
            nameof(NavRgb) => (Rgb, "Nav.Rgb", "Tab.Rgb.Sub"),
            nameof(NavPower) => (Power, "Nav.Performance", "Tab.Power.Sub"),
            nameof(NavAbout) => (About, "Nav.About", "Tab.About.Sub"),
            nameof(NavSettings) => (SettingsTab, "Nav.Settings", "Tab.Settings.Sub"),
            _ => (_dashboard, "Nav.Dashboard", "Tab.Dashboard.Sub"),
        };

        TabHost.Content = target.view;
        _titleKey = target.titleKey;
        _subKey = target.subKey;
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        TabTitle.Text = Loc.T(_titleKey);
        TabSubtitle.Text = Loc.T(_subKey);
    }
}
