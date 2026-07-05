using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GamingCenter.UI.Hardware;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class DashboardView : UserControl
{
    private HardwareMonitor? _monitor;
    private DispatcherTimer? _timer;
    private readonly IFanService _fan = new LocalFanService();
    private readonly NetworkMeter _net = new();
    private bool _noticeShown;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_timer is not null) return;

        try { _monitor = new HardwareMonitor(); }
        catch { ShowNotice("Sensor access needs elevation — live telemetry is unavailable in this session."); }

        // Active fan mode (from EC via the fan service). Duty/RPM and the curve
        // live on the Fan tab; the dashboard only echoes the current mode.
        try { FanMode.Text = FriendlyMode(await _fan.GetModeAsync() ?? "auto"); }
        catch { FanMode.Text = "—"; }

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
        // Network throughput is independent of the sensor library.
        _net.Sample();
        NetDown.Text = NetworkMeter.FormatBitsPerSec(_net.DownBytesPerSec);
        NetUp.Text = NetworkMeter.FormatBitsPerSec(_net.UpBytesPerSec);

        if (_monitor is null) return;

        Telemetry t;
        try { t = _monitor.GetTelemetry(); }
        catch { return; }

        // ---- CPU tile ----
        CpuGauge.Load = t.CpuLoadTotal ?? 0;
        CpuGauge.TempC = t.CpuTempC; // null → gauge shows load-only center + "temp n/a"
        CpuClock.Text = Mhz(t.CpuClockMhz);

        // ---- GPU tile ----
        GpuGauge.Load = t.GpuLoad ?? 0;
        GpuGauge.TempC = t.GpuTempC;
        GpuName.Text = t.GpuName ?? "—";
        GpuClock.Text = Mhz(t.GpuClockMhz);
        GpuPower.Text = Watts(t.GpuPowerW);
        GpuHotSpot.Text = t.GpuHotSpotC is double hs ? $"{hs:0}°" : "—";

        // ---- Memory: RAM / Commit / VRAM ----
        SetCapacity(RamBar, RamPct, RamText, t.RamLoad, t.RamUsedGb, t.RamTotalGb, "GB");
        SetCapacity(SwapBar, SwapPct, SwapText, t.SwapLoad, t.SwapUsedGb, t.SwapTotalGb, "GB");

        if (t.GpuVramUsedMb is double vu && t.GpuVramTotalMb is double vt && vt > 0)
        {
            double frac = Math.Clamp(vu / vt, 0, 1);
            VramBar.Fraction = frac;
            VramPct.Text = $"{frac * 100:0}%";
            // Show MB below 1 GB so light idle use (e.g. 113 MB) doesn't read as "0.0 GB".
            VramText.Text = vu < 1024
                ? $"{vu:0} MB / {vt / 1024.0:0.0} GB"
                : $"{vu / 1024.0:0.0} / {vt / 1024.0:0.0} GB";
        }
        else { VramPct.Text = "—"; VramText.Text = "n/a"; }

        // Safety net: only if the CPU exposes nothing at all.
        if (t.CpuTempC is null && t.CpuClockMhz is null && t.CpuLoadTotal is null && !_noticeShown)
            ShowNotice("CPU sensors are unavailable in this session.");
    }

    private void SetCapacity(Controls.CapacityBar bar, TextBlock pct, TextBlock text,
        double? loadPct, double? usedGb, double? totalGb, string unit)
    {
        if (loadPct is double p)
        {
            bar.Fraction = Math.Clamp(p / 100.0, 0, 1);
            pct.Text = $"{p:0}%";
        }
        else { pct.Text = "—"; }

        text.Text = usedGb is double u && totalGb is double tot
            ? $"{u:0.0} / {tot:0.0} {unit}"
            : "n/a";
    }

    private static string Mhz(double? v) => v is double d && d > 0 ? $"{d:0} MHz" : "—";
    private static string Watts(double? v) => v is double d && d > 0 ? $"{d:0.0} W" : "—";

    private static string FriendlyMode(string mode) => mode.ToLowerInvariant() switch
    {
        "boost" => "Boost",
        "custom" => "Custom",
        "auto" => "Auto",
        var m when m.StartsWith("l") => m.ToUpperInvariant(),
        _ => mode,
    };

    private void ShowNotice(string message)
    {
        _noticeShown = true;
        SensorNoticeText.Text = message;
        SensorNotice.Visibility = Visibility.Visible;
    }
}
