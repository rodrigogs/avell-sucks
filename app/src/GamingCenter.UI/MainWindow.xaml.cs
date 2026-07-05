using System;
using System.Windows;

namespace GamingCenter.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // WindowStartupLocation="CenterScreen" produced Top/Left = NaN under the RDP session,
        // parking the window off-screen. Position explicitly against the real work area instead.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            // Center within the work area, but clamp so the window is always fully on-screen.
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
            // As a last resort, land at a known-visible spot.
            Left = 80;
            Top = 80;
        }
    }

    private void OnTestClick(object sender, RoutedEventArgs e) => StatusLine.Text = "Button works @ " + DateTime.Now.ToString("HH:mm:ss");
}
