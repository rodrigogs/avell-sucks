using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GamingCenter.UI.Controls;
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

    private static readonly Brush Ink2 = new SolidColorBrush(Color.FromRgb(0xB6, 0xA0, 0xE0));

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

        // Active fan mode (from EC via the fan service). This platform has no
        // tachometer, so we show mode + duty (from the custom curve) — never RPM.
        try
        {
            var mode = await _fan.GetModeAsync() ?? "auto";
            FanMode.Text = FriendlyMode(mode);
            await ShowFanDuty(mode);
        }
        catch { FanMode.Text = "—"; FanDuty.Text = "—"; }

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

        if (_monitor is null) { Trend.Push(null, null); return; }

        Telemetry t;
        try { t = _monitor.GetTelemetry(); }
        catch { return; }

        // ---- CPU tile ----
        CpuGauge.Load = t.CpuLoadTotal ?? 0;
        CpuGauge.TempC = t.CpuTempC; // null → gauge shows load-only center + "temp n/a"
        CpuClock.Text = Mhz(t.CpuClockMhz);
        CpuPower.Text = Watts(t.CpuPowerW);
        SetStatus(CpuStatusPill, CpuStatus, DeriveCpuStatus(t));

        // ---- GPU tile ----
        GpuGauge.Load = t.GpuLoad ?? 0;
        GpuGauge.TempC = t.GpuTempC;
        GpuName.Text = t.GpuName ?? "—";
        GpuClock.Text = Mhz(t.GpuClockMhz);
        GpuPower.Text = Watts(t.GpuPowerW);
        GpuHotSpot.Text = t.GpuHotSpotC is double hs ? $"{hs:0}°" : "—";

        // ---- Trend (temperature only) ----
        Trend.Push(t.CpuTempC, t.GpuTempC);

        // ---- Memory: RAM / Commit / VRAM ----
        SetCapacity(RamBar, RamPct, RamText, t.RamLoad, t.RamUsedGb, t.RamTotalGb, "GB");
        SetCapacity(SwapBar, SwapPct, SwapText, t.SwapLoad, t.SwapUsedGb, t.SwapTotalGb, "GB");

        if (t.GpuVramUsedMb is double vu && t.GpuVramTotalMb is double vt && vt > 0)
        {
            double frac = Math.Clamp(vu / vt, 0, 1);
            VramBar.Fraction = frac;
            VramPct.Text = $"{frac * 100:0}%";
            VramText.Text = $"{vu / 1024.0:0.0} / {vt / 1024.0:0.0} GB";
        }
        else { VramPct.Text = "—"; VramText.Text = "n/a"; }

        // Honest note when the CPU can't report temperature on this platform.
        if (t.CpuTempC is null && t.CpuClockMhz is null && t.CpuPowerW is null && !_noticeShown)
            ShowNotice("This CPU reports load and temperature only; clock and package power aren't exposed on this platform and read “n/a”.");
    }

    // ---- Derived status chip ----
    private static (string text, Color color) DeriveCpuStatus(Telemetry t)
    {
        double load = t.CpuLoadTotal ?? 0;
        if (t.CpuTempC is double temp && Thermal.BandFor(temp, ThermalKind.Cpu) is ThermalBand.Hot or ThermalBand.Critical)
            return ("Hot", Thermal.Hot);
        if (load >= 90) return ("Maxed", Thermal.Warm);
        if (load >= 40) return ("Active", Thermal.Cold);
        return ("Idle", Color.FromRgb(0x7C, 0x6A, 0xA6));
    }

    private void SetStatus(Border pill, TextBlock label, (string text, Color color) s)
    {
        label.Text = s.text;
        label.Foreground = new SolidColorBrush(s.color);
        pill.Background = new SolidColorBrush(Color.FromArgb(0x26, s.color.R, s.color.G, s.color.B));
    }

    private void SetCapacity(CapacityBar bar, TextBlock pct, TextBlock text,
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

    /// <summary>
    /// Shows fan duty (PWM %) — the honest number this platform exposes — instead
    /// of a tachometer RPM, which neither this app nor the OEM can read here.
    /// </summary>
    private async System.Threading.Tasks.Task ShowFanDuty(string mode)
    {
        switch (mode.ToLowerInvariant())
        {
            case "auto":
                FanDuty.Text = "Auto";
                FanDutyUnit.Text = "";
                FanNote.Text = "Firmware-controlled · no tachometer (RPM) on this platform";
                return;
            case "boost":
                FanDuty.Text = "100";
                FanDutyUnit.Text = "% duty";
                FanNote.Text = "Max cooling · no tachometer (RPM) on this platform";
                return;
        }

        // Custom / L1–L5: report the highest curve PWM as duty (0–140 → %).
        try
        {
            var curve = await _fan.GetCurveAsync();
            int maxPwm = 0;
            foreach (var p in curve) maxPwm = Math.Max(maxPwm, p.Pwm);
            FanDuty.Text = $"{(int)Math.Round(maxPwm / 140.0 * 100)}";
            FanDutyUnit.Text = "% duty";
            FanNote.Text = "Custom curve peak · no tachometer (RPM) on this platform";
        }
        catch { FanDuty.Text = "—"; FanDutyUnit.Text = ""; }
    }

    private void ShowNotice(string message)
    {
        _noticeShown = true;
        SensorNoticeText.Text = message;
        SensorNotice.Visibility = Visibility.Visible;
    }
}
