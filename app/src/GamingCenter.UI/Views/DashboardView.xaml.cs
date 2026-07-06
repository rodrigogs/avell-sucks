using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingCenter.UI.Controls;
using GamingCenter.UI.Hardware;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class DashboardView : UserControl
{
    private readonly SensorPump _pump;
    private readonly IFanService _fan = new LocalFanService();
    private readonly NetworkMeter _net = new();
    private readonly DiskMeter _disk = new();
    private bool _started;

    // Disk sampling is slow I/O (off-thread) and storage barely moves, so it runs
    // every Nth pump tick rather than every second. Guarded so overlapping
    // samples never pile up if a disk is briefly slow.
    private const int DiskEveryTicks = 10; // ~10 s at 1 Hz
    private int _tick;
    private bool _diskBusy;

    // Per-drive identity color, assigned by enumeration order so each drive keeps
    // a stable hue. Color is identity only — bar severity still carries fill.
    private static readonly Color[] DriveColors =
    {
        Brand.Cyan, Brand.Violet, Brand.Magenta,
        Color.FromRgb(0x34, 0xE5, 0xA0), // ok-green
        Color.FromRgb(0xF4, 0xC0, 0x4A), // warn-amber
    };
    private readonly Dictionary<string, DiskRow> _diskRows = new();

    // The pump is owned and disposed by MainWindow and shared with the Fan view;
    // this view only subscribes/unsubscribes around its own visibility.
    public DashboardView(SensorPump pump)
    {
        _pump = pump;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _pump.Tick += OnTelemetry;

        if (!_started)
        {
            _started = true;

            // Active fan profile (from EC via the fan service). Duty/RPM and the
            // curve live on the Fan tab; the dashboard echoes the current profile.
            try { ShowCooling(await _fan.GetModeAsync() ?? "auto"); }
            catch { ShowCooling(null); }
        }

        // Idempotent: opens the ring-0 monitor on first call, no-ops afterwards.
        _pump.Start();

        if (!_pump.SensorsAvailable)
            ShowNotice("Sensor access needs elevation — live telemetry is unavailable in this session.");

        // First disk snapshot right away (off-thread); refreshed on a slow cadence.
        await SampleDiskAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Stop receiving ticks while off-screen; the window owns disposal.
        _pump.Tick -= OnTelemetry;
    }

    private async void OnTelemetry(Telemetry? t)
    {
        // Network throughput is independent of the sensor library.
        _net.Sample();
        NetDown.Text = NetworkMeter.FormatBitsPerSec(_net.DownBytesPerSec);
        NetUp.Text = NetworkMeter.FormatBitsPerSec(_net.UpBytesPerSec);

        // Disk: slow off-thread sample every Nth tick (storage moves slowly).
        if (++_tick % DiskEveryTicks == 0) await SampleDiskAsync();

        if (t is null) return;

        // ---- CPU tile ----
        CpuName.Text = t.CpuName ?? "—";
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
            VramBar.Fraction = Math.Clamp(vu / vt, 0, 1);
            VramPct.Text = $"{vu / vt * 100:0}%";
            // Show MB below 1 GB so light idle use (e.g. 113 MB) doesn't read as "0.0 GB".
            VramText.Text = vu < 1024
                ? $"{vu:0} MB / {vt / 1024.0:0.0} GB"
                : $"{vu / 1024.0:0.0} / {vt / 1024.0:0.0} GB";
        }
        else { VramPct.Text = "—"; VramText.Text = "n/a"; }
    }

    private static void SetCapacity(Controls.CapacityBar bar, TextBlock pct, TextBlock text,
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

    // The active cooling profile as a status instrument: label + one-line meaning
    // + an accent that tracks intensity (cyan = balanced, magenta = max cooling,
    // violet = user/fixed). No pill — the tinted glyph carries the color.
    private void ShowCooling(string? mode)
    {
        var m = mode?.Trim().ToLowerInvariant();
        (string label, string hint, Color accent) = m switch
        {
            "auto"   => ("Auto", "Balances noise and temperature", Brand.Cyan),
            "boost"  => ("Boost", "Maximum cooling — fans run cold", Brand.Magenta),
            "custom" => ("Custom", "Following your temperature curve", Brand.Violet),
            not null when m.StartsWith("l") && m.Length <= 3
                     => (m.ToUpperInvariant(), "Fixed fan level", Brand.Violet),
            null     => ("—", "Fan mode unavailable", Brand.Ink3),
            _        => (mode!, "Active fan profile", Brand.Cyan),
        };

        FanMode.Text = label;
        CoolHint.Text = hint;

        var accentBrush = Brand.Frozen(accent);
        FanMode.Foreground = accentBrush;
        CoolIcon.Foreground = accentBrush;
        CoolIconBg.Background = Brand.Frozen(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
    }

    // Off-thread disk sample → UI update. Overlap-guarded so a slow disk can't
    // queue up samples; failures leave the last-known rows untouched.
    private async System.Threading.Tasks.Task SampleDiskAsync()
    {
        if (_diskBusy) return;
        _diskBusy = true;
        try
        {
            var drives = await _disk.SampleAsync();
            UpdateDisk(drives);
        }
        catch { /* transient disk error — keep prior rows */ }
        finally { _diskBusy = false; }
    }

    private void UpdateDisk(IReadOnlyList<DriveUsage> drives)
    {
        if (drives.Count == 0)
        {
            DiskFree.Text = "n/a";
            return;
        }

        // Header headline: total free across all fixed drives (the "available" ask).
        long totalFree = 0;
        foreach (var d in drives) totalFree += d.FreeBytes;
        DiskFree.Text = $"{Bytes(totalFree)} free";

        // Build/refresh one row per drive, keyed by name so rows are reused (no
        // flicker) and a drive that vanishes (USB unplug) is dropped.
        var seen = new HashSet<string>();
        for (int i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            seen.Add(d.Name);
            if (!_diskRows.TryGetValue(d.Name, out var row))
            {
                var color = DriveColors[i % DriveColors.Length];
                row = new DiskRow(d.Name, color);
                _diskRows[d.Name] = row;
                DiskRows.Children.Add(row.Root);
            }
            row.Update(d);
        }

        // Remove rows for drives no longer present.
        if (_diskRows.Count > seen.Count)
        {
            var gone = new List<string>();
            foreach (var kv in _diskRows) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var name in gone)
            {
                DiskRows.Children.Remove(_diskRows[name].Root);
                _diskRows.Remove(name);
            }
        }
    }

    private static string Bytes(long b)
    {
        const double G = 1024d * 1024 * 1024;
        double gb = b / G;
        return gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
    }

    private void ShowNotice(string message)
    {
        SensorNoticeText.Text = message;
        SensorNotice.Visibility = Visibility.Visible;
    }

    // One disk drive's row: a color chip (drive identity) + letter, a reused
    // CapacityBar (severity fill), and used/total text. Color tags the drive;
    // the bar still recolors amber/red as it fills, so a full disk reads as risk.
    private sealed class DiskRow
    {
        public readonly FrameworkElement Root;
        private readonly CapacityBar _bar;
        private readonly TextBlock _text;

        public DiskRow(string name, Color color)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

            var head = new DockPanel();
            var chip = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = Brand.Frozen(color),
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = name.Replace(":", ""),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brand.Frozen(Brand.Bg),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(chip, Dock.Left);
            head.Children.Add(chip);

            _text = new TextBlock
            {
                Text = "—",
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = 11,
                Foreground = Brand.Frozen(Brand.Ink3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            head.Children.Add(_text);

            _bar = new CapacityBar { Height = 7, Margin = new Thickness(0, 5, 0, 0), WarnAt = 0.85, CriticalAt = 0.95 };

            outer.Children.Add(head);
            outer.Children.Add(_bar);
            Root = outer;
        }

        public void Update(DriveUsage d)
        {
            _bar.Fraction = d.UsedFraction;
            _text.Text = $"{Bytes(d.FreeBytes)} free";
        }
    }
}
