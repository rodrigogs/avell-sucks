using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Hardware;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Views;

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

        // ModeHint is set imperatively (Text=) once a mode is picked, which drops
        // its {loc:Tr} binding. Re-apply it for the current mode on a live language
        // change so it doesn't get stuck in the old language. FanView is a cached,
        // app-lifetime view (MainWindow lazy prop), so no unsubscribe is needed.
        Loc.Instance.PropertyChanged += (_, _) => RefreshModeHint();
    }

    // The currently-selected fan mode, or "auto" as the neutral default.
    private string CurrentMode() =>
        ModeBoost.IsChecked == true ? "boost" :
        ModeCustom.IsChecked == true ? "custom" :
        ModeL1.IsChecked == true ? "l1" :
        ModeL2.IsChecked == true ? "l2" :
        ModeL3.IsChecked == true ? "l3" :
        ModeL4.IsChecked == true ? "l4" :
        ModeL5.IsChecked == true ? "l5" : "auto";

    private static string HintFor(string mode) => mode switch
    {
        "auto" => Loc.T("Fan.Hint.Auto"),
        "boost" => Loc.T("Fan.Hint.Boost"),
        "custom" => Loc.T("Fan.Hint.Custom"),
        _ => string.Format(Loc.T("Fan.Hint.Fixed"), mode.ToUpperInvariant()),
    };

    // Re-localize the hint for whatever mode is selected. If no mode is checked yet
    // (fresh load), leave the live {loc:Tr Fan.Hint.Default} binding intact.
    private void RefreshModeHint()
    {
        if (ModeHint is null) return;
        if (ModeAuto.IsChecked != true && ModeBoost.IsChecked != true && ModeCustom.IsChecked != true
            && ModeL1.IsChecked != true && ModeL2.IsChecked != true && ModeL3.IsChecked != true
            && ModeL4.IsChecked != true && ModeL5.IsChecked != true)
            return;
        ModeHint.Text = HintFor(CurrentMode());
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Trace($"FanView.OnLoaded (initialized={_initialized}, monitor={(_monitor is null ? "null" : "set")})");
        // Live temperature trend, for thermal context while tuning the curve.
        _pump.Tick += OnTelemetry;
        _pump.Start();

        // One-time initial state load.
        if (!_initialized)
        {
            _initialized = true;
            _loading = true;
            if (!_fan.WritesEnabled)
                GateNotice.Visibility = Visibility.Visible;

            var mode = await _fan.GetModeAsync();
            SelectMode(mode ?? "auto");
            _monitor?.NoteLocalWrite(mode ?? "auto"); // seed baseline with device state
            var curve = await _fan.GetCurveAsync();
            Curve.SetPoints(curve);
            _loading = false;
        }

        // Reconciler must (re)start on EVERY view activation — OnUnloaded stops it
        // on each tab switch, and this view instance is reused, so gating this
        // behind _initialized would leave the monitor dead after the first visit.
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
        Toaster.Show(WriteState.Verified, string.Format(Loc.T("Common.ChangedOnDevice"), ModeLabel(mode)));
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

        ModeHint.Text = HintFor(mode);

        _curveWrite.Cancel(); // a mode press supersedes a pending curve write
        _monitor?.Suspend();  // don't let the reconciler yank the selection while this settles
        Toaster.Show(WriteState.Pending, ModeLabel(mode));
        var result = await _fan.SetModeAsync(mode);
        Toaster.Show(result.State, string.Format(Loc.T("Fan.ModeSet"), ModeLabel(mode)), result.Error);
        _monitor?.Resume(result.State == WriteState.Verified ? mode : (await _fan.GetModeAsync() ?? "auto"));
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
        _monitor?.Suspend(); // curve write flips mode to custom; let it settle
        Toaster.Show(WriteState.Pending, Loc.T("Fan.Curve"));
        var result = await _fan.SetCurveAsync(Curve.Points.ToArray());
        Toaster.Show(result.State, Loc.T("Fan.CurveApplied"), result.Error);
        if (result.State == WriteState.Verified && !_loading)
            SelectMode("custom");
        _monitor?.Resume(result.State == WriteState.Verified ? "custom" : (await _fan.GetModeAsync() ?? "auto"));
    }

    // The one explicit escape hatch: hand the fan back to Auto.
    private async void OnReset(object sender, RoutedEventArgs e)
    {
        _curveWrite.Cancel();
        _loading = true;
        var curve = await _fan.GetCurveAsync();
        Curve.SetPoints(curve);
        _loading = false;

        _monitor?.Suspend();
        Toaster.Show(WriteState.Pending, ModeLabel("auto"));
        var result = await _fan.SetModeAsync("auto");
        if (result.State == WriteState.Verified) SelectMode("auto");
        Toaster.Show(result.State, string.Format(Loc.T("Fan.ModeSet"), ModeLabel("auto")), result.Error);
        _monitor?.Resume(result.State == WriteState.Verified ? "auto" : (await _fan.GetModeAsync() ?? "auto"));
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "auto" => Loc.T("Fan.Label.Auto"),
        "boost" => Loc.T("Fan.Label.Boost"),
        "custom" => Loc.T("Fan.Label.Custom"),
        _ => string.Format(Loc.T("Fan.Label.Fixed"), mode.ToUpperInvariant()),
    };
}
