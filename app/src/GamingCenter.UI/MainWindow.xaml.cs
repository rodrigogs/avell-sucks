using System;
using System.Windows;
using System.Windows.Controls;
using GamingCenter.UI.Views;

namespace GamingCenter.UI;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboard = new();
    private readonly FanView _fan = new();
    private readonly RgbView _rgb = new();
    private readonly PowerView _power = new();

    public MainWindow()
    {
        InitializeComponent();
        // Keep the on-screen placement fix: CenterScreen produced Top/Left=NaN
        // under the RDP session, parking the window off-screen.
        Loaded += OnLoaded;
        TabHost.Content = _dashboard;
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
            nameof(NavFan) => (_fan, "Fan", "Performance modes and the custom temperature curve"),
            nameof(NavRgb) => (_rgb, "RGB", "Keyboard lighting — effects, color and zones"),
            nameof(NavPower) => (_power, "Power", "CPU power limits — PL1 / PL2 / Tau"),
            _ => (_dashboard, "Dashboard", "Live telemetry from EC, WMI and onboard sensors"),
        };

        TabHost.Content = target.view;
        TabTitle.Text = target.title;
        TabSubtitle.Text = target.subtitle;
    }
}
