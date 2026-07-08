using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GamingCenter.UI.Controls;
using GamingCenter.UI.Hardware;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class FanView : UserControl
{
    private readonly IFanService _fan = HardwareServices.CreateFanService();
    private readonly SensorPump _pump;
    private readonly Debouncer _curveWrite = new(450);
    // Reconciler: reflects fan-mode changes made outside our app (OEM app / Fn key).
    // Null when there's no real EC backend (writes off / stub) — nothing to poll.
    private readonly FanStateMonitor? _monitor;
    private bool _loading;
    private bool _initialized;

    // The pump is owned and disposed by MainWindow and shared with the Dashboard;
    // this view only subscribes/unsubscribes around its own visibility.
    public FanView(SensorPump pump)
    {
        _pump = pump;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // Live edit: dragging a curve point re-applies on settle (debounced).
        Curve.CurveChanged += OnCurveEdited;

        var backend = HardwareServices.EcBackend();
        if (backend is not null)
        {
            _monitor = new FanStateMonitor(backend);
            _monitor.ExternalModeChanged += OnExternalModeChanged;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Live temperature trend, for thermal context while tuning the curve.
        _pump.Tick += OnTelemetry;
        _pump.Start();

        if (_initialized) return;
        _initialized = true;

        _loading = true;
        if (!_fan.WritesEnabled)
            GateNotice.Visibility = Visibility.Visible;

        var mode = await _fan.GetModeAsync();
        SelectMode(mode ?? "auto");
        _monitor?.NoteLocalWrite(mode ?? "auto"); // seed baseline with current device state

        var curve = await _fan.GetCurveAsync();
        Curve.SetPoints(curve);
        _loading = false;

        // Start reflecting external changes once the initial state is loaded.
        _monitor?.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Stop receiving ticks while off-screen; the window owns disposal.
        _pump.Tick -= OnTelemetry;
        _monitor?.Stop();
    }

    // Device changed outside our app (OEM Gaming Center, or the physical Fn key):
    // reflect the new mode in the UI without a self-authored write toast.
    private void OnExternalModeChanged(string mode)
    {
        App.Trace($"FanStateMonitor: external mode change detected → {mode}");
        _loading = true;            // don't let SelectMode re-trigger a write
        SelectMode(mode);
        _loading = false;
        Toaster.Show(WriteState.Verified, ModeLabel(mode) + " · changed on device");
    }

    private void OnTelemetry(Telemetry? t) => Curve.PushTemperature(t?.CpuTempC, t?.GpuTempC);

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

    // Selecting a mode actuates immediately — no Apply button.
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

        _curveWrite.Cancel(); // a mode press supersedes a pending curve write
        _monitor?.NoteLocalWrite(mode); // our own write — don't report it back as external
        Toaster.Show(WriteState.Pending, ModeLabel(mode));
        var result = await _fan.SetModeAsync(mode);
        Toaster.Show(result.State, ModeLabel(mode) + " set", result.Error);
    }

    // Dragging a curve point re-applies the custom curve on settle.
    private void OnCurveEdited(object? sender, EventArgs e)
    {
        if (_loading) return;
        Toaster.Clear();
        _curveWrite.Trigger(ApplyCurveNow);
    }

    private async void ApplyCurveNow()
    {
        _monitor?.NoteLocalWrite("custom"); // curve write flips mode to custom
        Toaster.Show(WriteState.Pending, "Fan curve");
        var result = await _fan.SetCurveAsync(Curve.Points.ToArray());
        Toaster.Show(result.State, "Fan curve applied", result.Error);
        if (result.State == WriteState.Verified && !_loading)
            SelectMode("custom");
    }

    // The one explicit escape hatch: hand the fan back to Auto.
    private async void OnReset(object sender, RoutedEventArgs e)
    {
        _curveWrite.Cancel();
        _loading = true;
        var curve = await _fan.GetCurveAsync();
        Curve.SetPoints(curve);
        _loading = false;

        _monitor?.NoteLocalWrite("auto");
        Toaster.Show(WriteState.Pending, ModeLabel("auto"));
        var result = await _fan.SetModeAsync("auto");
        if (result.State == WriteState.Verified) SelectMode("auto");
        Toaster.Show(result.State, ModeLabel("auto") + " set", result.Error);
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "auto" => "Auto fan",
        "boost" => "Boost fan",
        "custom" => "Custom fan",
        _ => $"Fan {mode.ToUpperInvariant()}",
    };
}
