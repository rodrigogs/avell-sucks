using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GamingCenter.UI.Hardware;

namespace GamingCenter.UI.Views;

public partial class DashboardView : UserControl
{
    private HardwareMonitor? _monitor;
    private DispatcherTimer? _timer;
    private bool _sensorsDegraded;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_timer is not null) return;

        try
        {
            _monitor = new HardwareMonitor();
        }
        catch
        {
            _sensorsDegraded = true;
            ShowNotice("Sensor access needs elevation. Live telemetry is unavailable in this session.");
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        _monitor?.Dispose();
        _monitor = null;
    }

    private void Poll()
    {
        if (_monitor is null)
        {
            // No sensor backend — keep the UI honest and readable with placeholder ticks.
            return;
        }

        try
        {
            _monitor.Update();
            var snap = _monitor.GetSnapshot();

            double? cpuTemp = FirstValue(snap, "Temperature", n => n.Contains("CPU") || n.Contains("Core") || n.Contains("Package"));
            double? gpuTemp = FirstValue(snap, "Temperature", n => n.Contains("GPU"));
            double? fanRpm = FirstValue(snap, "Fan", _ => true);
            double? cpuLoad = FirstValue(snap, "Load", n => n.Contains("CPU") || n.Contains("Total"));
            double? gpuLoad = FirstValue(snap, "Load", n => n.Contains("GPU") || n.Contains("Core"));
            double? memUsed = FirstValue(snap, "Data", n => n.Contains("Memory Used"));
            double? memAvail = FirstValue(snap, "Data", n => n.Contains("Memory Available"));

            if (cpuTemp is { } ct) { CpuGauge.Value = ct; CpuTrend.Push(ct); }
            if (gpuTemp is { } gt) { GpuGauge.Value = gt; GpuTrend.Push(gt); }
            if (fanRpm is { } fr) FanGauge.Value = fr;

            if (cpuLoad is { } cl) SetBar(CpuLoadBar, CpuLoadText, cl, 100, cl.ToString("0") + "%");
            if (gpuLoad is { } gl) SetBar(GpuLoadBar, GpuLoadText, gl, 100, gl.ToString("0") + "%");

            if (memUsed is { } mu && memAvail is { } ma && mu + ma > 0)
            {
                double total = mu + ma;
                SetBar(MemBar, MemText, mu, total, $"{mu:0.0}/{total:0.0} GB");
            }

            bool anySensor = cpuTemp is not null || gpuTemp is not null || fanRpm is not null;
            if (!anySensor && !_sensorsDegraded)
            {
                _sensorsDegraded = true;
                ShowNotice("No CPU/GPU sensors reported. Run elevated for full ring-0 telemetry.");
            }
        }
        catch
        {
            // Transient sensor read failure — leave last values in place.
        }
    }

    private static double? FirstValue(HardwareSnapshot snap, string sensorType, Func<string, bool> nameMatch)
    {
        var reading = snap.Sensors.FirstOrDefault(s =>
            s.SensorType == sensorType && (nameMatch(s.Name) || nameMatch(s.Hardware)));
        return reading?.Value;
    }

    private void SetBar(System.Windows.Controls.Border bar, System.Windows.Controls.TextBlock text, double value, double max, string label)
    {
        text.Text = label;
        if (bar.Parent is FrameworkElement track && track.ActualWidth > 0)
        {
            double frac = max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
            bar.Width = track.ActualWidth * frac;
        }
    }

    private void ShowNotice(string message)
    {
        SensorNoticeText.Text = message;
        SensorNotice.Visibility = Visibility.Visible;
    }
}
