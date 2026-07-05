using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GamingCenter.UI.Hardware;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class FanView : UserControl
{
    private readonly IFanService _fan = new LocalFanService();
    private readonly SensorPump _pump = new();
    private bool _loading;

    public FanView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Curve.CurveChanged += (_, _) => Badge.State = WriteState.Idle;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        if (!_fan.WritesEnabled)
            GateNotice.Visibility = Visibility.Visible;

        var mode = await _fan.GetModeAsync();
        SelectMode(mode ?? "auto");

        var curve = await _fan.GetCurveAsync();
        Curve.SetPoints(curve);
        _loading = false;

        // Live temperature trend, for thermal context while tuning the curve.
        _pump.Tick += OnTelemetry;
        _pump.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pump.Tick -= OnTelemetry;
        _pump.Dispose();
    }

    private void OnTelemetry(Telemetry? t) => Trend.Push(t?.CpuTempC, t?.GpuTempC);

    private void SelectMode(string mode)
    {
        RadioButton? rb = mode.ToLowerInvariant() switch
        {
            "boost" => ModeBoost,
            "custom" => ModeCustom,
            "l1" => ModeL1,
            "l2" => ModeL2,
            "l3" => ModeL3,
            "l4" => ModeL4,
            "l5" => ModeL5,
            _ => ModeAuto,
        };
        if (rb is not null) rb.IsChecked = true;
    }

    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb) return;
        string mode = rb.Name.Replace("Mode", "").ToLowerInvariant();

        ModeHint.Text = mode switch
        {
            "auto" => "Auto balances noise and temperature automatically.",
            "boost" => "Boost runs the fans cold — maximum cooling, loudest.",
            "custom" => "Custom follows the temperature curve below.",
            _ => $"Fixed level {mode.ToUpperInvariant()} — a constant fan intensity step.",
        };

        Badge.State = WriteState.Pending;
        var result = await _fan.SetModeAsync(mode);
        Badge.State = result.State;
        Badge.Message = result.Error ?? "";
    }

    private async void OnApplyCurve(object sender, RoutedEventArgs e)
    {
        Badge.State = WriteState.Pending;
        Badge.Message = "";
        var result = await _fan.SetCurveAsync(Curve.Points.ToArray());
        Badge.State = result.State;
        Badge.Message = result.Error ?? "";
        if (result.State == WriteState.Verified)
            SelectMode("custom");
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var curve = await _fan.GetCurveAsync();
        Curve.SetPoints(curve);
        Badge.State = WriteState.Idle;
    }
}
