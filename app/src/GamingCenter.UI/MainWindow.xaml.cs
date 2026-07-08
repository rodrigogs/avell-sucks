using System;
using System.Windows;
using System.Windows.Controls;
using GamingCenter.UI.Hardware;
using GamingCenter.UI.Startup;
using GamingCenter.UI.Views;

namespace GamingCenter.UI;

public partial class MainWindow : Window
{
    private bool _reallyClosing;
    private readonly SensorPump _pump = new();
    private readonly DashboardView _dashboard;

    // Fan/RGB/Performance are built on FIRST navigation, not at startup. Only the
    // Dashboard is shown initially; eagerly constructing the other three parsed
    // their XAML and spun up their monitors + EC reads (PowerView.BuildModeCards
    // fires 4 preset reads; FanView/PowerView create polling monitors) before the
    // window even appeared. Lazy construction keeps first-open cheap.
    private FanView? _fan;
    private RgbView? _rgb;
    private PowerView? _power;
    private FanView Fan => _fan ??= new FanView(_pump);
    private RgbView Rgb => _rgb ??= new RgbView();
    private PowerView Power => _power ??= new PowerView();

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

        // Optional deep-link to a tab for screenshot validation (GC_START_TAB=fan|rgb|power).
        var startTab = Environment.GetEnvironmentVariable("GC_START_TAB")?.Trim().ToLowerInvariant();
        RadioButton? initial = startTab switch
        {
            "fan" => NavFan,
            "rgb" => NavRgb,
            "power" => NavPower,
            _ => null,
        };
        if (initial is not null) initial.IsChecked = true;

        AutoStartItem.IsChecked = AutoStart.IsEnabled();
    }

    // ===== System tray =====

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Minimize hides to tray instead of the taskbar.
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
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

    private void OnToggleAutoStart(object sender, RoutedEventArgs e)
    {
        bool enable = AutoStartItem.IsChecked;
        AutoStart.Set(enable);
        // Reflect the real state in case the registry write failed.
        AutoStartItem.IsChecked = AutoStart.IsEnabled();
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
            WindowState = WindowState.Normal;
            Topmost = true;
            Activate();
            Topmost = false;
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

        (UserControl view, string title, string subtitle) target = rb.Name switch
        {
            nameof(NavFan) => (Fan, "Fan", "Fan modes and the custom temperature curve"),
            nameof(NavRgb) => (Rgb, "RGB", "Keyboard lighting — effects, color and zones"),
            nameof(NavPower) => (Power, "Performance", "Power modes — Windows plan and CPU power envelope"),
            _ => (_dashboard, "Dashboard", "Live telemetry from EC, WMI and onboard sensors"),
        };

        TabHost.Content = target.view;
        TabTitle.Text = target.title;
        TabSubtitle.Text = target.subtitle;
    }
}
