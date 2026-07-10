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
    private readonly LoadingGate _loading = new();
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
        // change so it doesn't get stuck in the old language. (RefreshModeHint is a
        // no-op until a mode is selected, so the immediate call is harmless.)
        Loc.OnCultureChanged(RefreshModeHint);
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

        // Re-evaluate every activation: the write gate is now live-toggleable
        // (Settings → Hardware writes) and this view is cached, so reconcile the
        // notice with the current gate on each revisit, not once on first load.
        GateNotice.Visibility = _fan.WritesEnabled ? Visibility.Collapsed : Visibility.Visible;

        // One-time initial state load.
        if (!_initialized)
        {
            _initialized = true;
            using (_loading.Begin())
            {
                var mode = await _fan.GetModeAsync();
                SelectMode(mode ?? "auto");
                _monitor?.NoteLocalWrite(mode ?? "auto"); // seed baseline with device state
                var curve = await _fan.GetCurveAsync();
                Curve.SetPoints(curve);
            }
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
        using (_loading.Begin())    // don't let SelectMode re-trigger a write
            SelectMode(mode);
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
        UpdateResetVisibility();
    }

    // "Reset to auto" is only meaningful when the fan has diverged from auto;
    // in auto the Auto chip already IS the state, so the button is redundant.
    private void UpdateResetVisibility()
    {
        if (ResetBtn is null) return;
        ResetBtn.Visibility = CurrentMode() == "auto" ? Visibility.Collapsed : Visibility.Visible;
    }

    // Run a fan write while the reconciler is suspended (so it can't yank the
    // selection mid-settle), with the standard Pending/result toast, then resume
    // the reconciler on the SETTLED mode: the intended mode if the write verified,
    // otherwise whatever the device actually reads back (or "auto" if unreadable).
    // This is the one subtle ternary the three write paths all share.
    private async System.Threading.Tasks.Task<ControlResult> WriteReconciledAsync(
        string settledMode, string pendingLabel, string doneLabel,
        System.Func<System.Threading.Tasks.ValueTask<ControlResult>> write)
    {
        _monitor?.Suspend();
        var result = await Toaster.Apply(pendingLabel, doneLabel, write);
        var settled = result.State == WriteState.Verified
            ? settledMode
            : (await _fan.GetModeAsync() ?? "auto");
        _monitor?.Resume(settled);

        // If the write didn't take (blocked/failed), snap the selection back to the
        // mode the device is actually in — never leave a chip selected on a mode we
        // didn't apply.
        if (!string.Equals(settled, settledMode, System.StringComparison.OrdinalIgnoreCase))
            using (_loading.Begin())
                SelectMode(settled);
        return result;
    }

    // Selecting a mode actuates immediately — no Apply button.
    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading.Active || sender is not RadioButton rb) return;
        string mode = rb.Name.Replace("Mode", "").ToLowerInvariant();

        ModeHint.Text = HintFor(mode);
        UpdateResetVisibility();

        _curveWrite.Cancel(); // a mode press supersedes a pending curve write
        await WriteReconciledAsync(
            mode, ModeLabel(mode), string.Format(Loc.T("Fan.ModeSet"), ModeLabel(mode)),
            () => _fan.SetModeAsync(mode));
    }

    // Dragging a curve point re-applies the custom curve on settle.
    private void OnCurveEdited(object? sender, EventArgs e)
    {
        if (_loading.Active) return;
        Toaster.Clear();
        _curveWrite.Trigger(ApplyCurveNow);
    }

    private async void ApplyCurveNow()
    {
        // A curve write flips the mode to custom; settle on "custom" if verified.
        var result = await WriteReconciledAsync(
            "custom", Loc.T("Fan.Curve"), Loc.T("Fan.CurveApplied"),
            () => _fan.SetCurveAsync(Curve.Points.ToArray()));
        if (result.State == WriteState.Verified && !_loading.Active)
            SelectMode("custom");
    }

    // The one explicit escape hatch: hand the fan back to Auto.
    private async void OnReset(object sender, RoutedEventArgs e)
    {
        _curveWrite.Cancel();
        using (_loading.Begin())
        {
            var curve = await _fan.GetCurveAsync();
            Curve.SetPoints(curve);
        }

        var result = await WriteReconciledAsync(
            "auto", ModeLabel("auto"), string.Format(Loc.T("Fan.ModeSet"), ModeLabel("auto")),
            () => _fan.SetModeAsync("auto"));
        if (result.State == WriteState.Verified) SelectMode("auto");
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "auto" => Loc.T("Fan.Label.Auto"),
        "boost" => Loc.T("Fan.Label.Boost"),
        "custom" => Loc.T("Fan.Label.Custom"),
        _ => string.Format(Loc.T("Fan.Label.Fixed"), mode.ToUpperInvariant()),
    };
}
