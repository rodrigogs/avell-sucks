using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Views;

public partial class PowerView : UserControl
{
    private readonly IPowerService _power = HardwareServices.CreatePowerService();
    private readonly Debouncer _limitWrite = new(450);
    // Reconciler: reflects power-plan changes made outside our app. Only active
    // when the real backend is live (elevated); null on the stub.
    private readonly PowerStateMonitor? _monitor;
    // Guard starts CLOSED and only opens at the end of OnLoaded, so nothing
    // written during construction / XAML init / initial hydration actuates the
    // EC. (A card auto-checking or a slider raising ValueChanged during
    // InitializeComponent must NOT trigger a hardware write.)
    // Starts suppressed: cards auto-checking / sliders raising ValueChanged during
    // InitializeComponent + initial hydration must not actuate the EC.
    private readonly LoadingGate _loading = new(startActive: true);

    public PowerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        if (HardwareServices.IsRealBackendActive)
        {
            _monitor = new PowerStateMonitor();
            _monitor.ExternalModeChanged += OnExternalModeChanged;
        }

        // Cards and the envelope label are set imperatively (not {loc:Tr}), so
        // build them now and rebuild on every runtime language change.
        Loc.OnCultureChanged(RelocalizeCards);
    }

    private void RelocalizeCards()
    {
        BuildModeCards();
        if (EnvelopeMode is not null) EnvelopeMode.Text = Meta(CurrentMode()).Name;
    }

    // ---- Mode metadata (name, one-line meaning, accent by intensity) ----
    private readonly record struct ModeMeta(string Name, string Tagline, Color Accent);

    private static ModeMeta Meta(PerformanceMode m) => m switch
    {
        PerformanceMode.Gaming   => new(Loc.T("Power.Gaming"), Loc.T("Power.Gaming.Tag"), Brand.Magenta),
        PerformanceMode.High     => new(Loc.T("Power.High"), Loc.T("Power.High.Tag"), Brand.Violet),
        PerformanceMode.Balanced => new(Loc.T("Power.Balanced"), Loc.T("Power.Balanced.Tag"), Brand.Cyan),
        PerformanceMode.Saving   => new(Loc.T("Power.Saving"), Loc.T("Power.Saving.Tag"), Brand.Ok),
        _ => new(m.ToString(), "", Brand.Cyan),
    };

    private static PerformanceMode ModeOf(RadioButton rb) => (string)rb.Tag switch
    {
        "Gaming" => PerformanceMode.Gaming,
        "High" => PerformanceMode.High,
        "Saving" => PerformanceMode.Saving,
        _ => PerformanceMode.Balanced,
    };

    private RadioButton CardFor(PerformanceMode m) => m switch
    {
        PerformanceMode.Gaming => ModeGaming,
        PerformanceMode.High => ModeHigh,
        PerformanceMode.Saving => ModeSaving,
        _ => ModeBalanced,
    };

    // Fill each card with name + tagline + its PL preset, so the trade-off is
    // legible before selecting. Presets come from the ACTIVE service (real
    // machine defaults when hardware-backed; stub presets otherwise) so the
    // cards never advertise watts the app won't apply.
    private async void BuildModeCards()
    {
        foreach (var m in new[] { PerformanceMode.Gaming, PerformanceMode.High, PerformanceMode.Balanced, PerformanceMode.Saving })
        {
            var meta = Meta(m);
            var preset = await _power.GetPresetAsync(m);
            var accent = Brand.Frozen(meta.Accent);

            var panel = new StackPanel();

            panel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 9, Height = 9, Fill = accent,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
            });

            panel.Children.Add(new TextBlock
            {
                Text = meta.Name,
                FontFamily = (FontFamily)Application.Current.FindResource("UiFontDisplay"),
                FontSize = (double)Application.Current.FindResource("FsH3"),
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
            });

            panel.Children.Add(new TextBlock
            {
                Text = meta.Tagline,
                Style = (Style)Application.Current.FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 10),
                MinHeight = 28,
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"{preset.Pl1Watts} / {preset.Pl2Watts} W",
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = 12,
                Foreground = Brand.Frozen(Brand.Ink),
            });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Power.PlLabel"),
                Style = (Style)Application.Current.FindResource("Caption"),
            });

            CardFor(m).Content = panel;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-evaluate every activation: the write gate is now live-toggleable
        // (Settings → Hardware writes), and this view is cached, so the notice must
        // reconcile with the current gate on each revisit, not latch on first load.
        GateNotice.Visibility = _power.WritesEnabled ? Visibility.Collapsed : Visibility.Visible;

        var state = await _power.GetAsync();

        // Hydrate the controls under a suppression scope EVERY activation: this view
        // is cached, so a revisit re-runs hydration; without the scope, setting the
        // card's IsChecked here (when the active mode changed while away) would fire
        // OnModeChecked as a "user action" and issue a spurious write. Begin() also
        // covers the ctor's startActive on first load; End() then clears it once.
        using (_loading.Begin())
        {
            CardFor(state.Mode).IsChecked = true;
            ShowEnvelope(state.Mode, state.Limits);
            LoadSliders(state.Limits);
        }
        _loading.End();

        // Seed the reconciler baseline from the ACTIVE SCHEME (same source the
        // monitor polls) so it doesn't fire a spurious "changed on device" toast
        // for the plan that was already active when the tab opened.
        if (_monitor is not null) await _monitor.SeedBaselineAsync();
        _monitor?.Start();
        App.Trace($"PowerView loaded: monitor={( _monitor is null ? "NULL(stub)" : "active")} mode={state.Mode}");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _monitor?.Stop();

    // Active Windows power plan changed outside our app: reflect it without a
    // self-authored write toast.
    private async void OnExternalModeChanged(PerformanceMode mode)
    {
        App.Trace($"PowerStateMonitor: external power-plan change detected → {mode}");
        var preset = await _power.GetPresetAsync(mode);
        using (_loading.Begin())
        {
            CardFor(mode).IsChecked = true;
            ShowEnvelope(mode, preset);
            LoadSliders(preset);
        }
        Toaster.Show(WriteState.Verified,
            string.Format(Loc.T("Common.ChangedOnDevice"), string.Format(Loc.T("Power.ModeName"), Meta(mode).Name)));
    }

    // ---- Mode selection: actuates immediately (no Apply button) ----
    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var mode = ModeOf(rb);

        // Capture the intent-to-write BEFORE any await. During load the gate is
        // active and checking a card fires this handler; the await below would
        // yield, the load could clear the gate, and on resume the guard would
        // wrongly read "user action" → a spurious write on simply opening the tab.
        // Latch it up front so an await can't corrupt it.
        var isUserAction = !_loading.Active;

        var preset = await _power.GetPresetAsync(mode);
        ShowEnvelope(mode, preset);

        // Load the sliders to match, without their ValueChanged writing back.
        // Checked can fire during InitializeComponent before later elements exist.
        if (Pl1Slider is not null)
            using (_loading.Begin())
                LoadSliders(preset);

        if (!isUserAction) return; // hydration selection — never write
        _limitWrite.Cancel(); // mode press supersedes a pending slider write

        // Suspend the reconciler across the write (a tick mid-settle mustn't yank
        // the card), then anchor the baseline on the SETTLED mode: the intended one
        // only if the write verified, otherwise the mode the device is actually in.
        // Seeding the baseline BEFORE the write (the old NoteLocalWrite) meant a
        // gate-blocked write left the baseline on a mode never applied, so the next
        // poll fired a phantom "changed on device" toast and reverted the card.
        _monitor?.Suspend();
        var result = await Toaster.Apply(
            string.Format(Loc.T("Power.ModeName"), Meta(mode).Name),
            string.Format(Loc.T("Power.ModeOn"), Meta(mode).Name),
            () => _power.SetModeAsync(mode));

        var settled = result.State == WriteState.Verified ? mode : (await _power.GetAsync()).Mode;
        _monitor?.Resume(settled);

        // If the write didn't take (blocked/failed), reflect the mode the machine is
        // actually in — never leave the UI selecting a mode we didn't apply.
        if (settled != mode)
            using (_loading.Begin())
            {
                var settledPreset = await _power.GetPresetAsync(settled);
                CardFor(settled).IsChecked = true;
                ShowEnvelope(settled, settledPreset);
                LoadSliders(settledPreset);
            }
    }

    private void ShowEnvelope(PerformanceMode mode, PowerLimits limits)
    {
        if (EnvelopeMode is null) return;
        var meta = Meta(mode);
        EnvelopeMode.Text = meta.Name;
        var accent = Brand.Frozen(meta.Accent);
        Pl1Value.Text = $"{limits.Pl1Watts} W"; Pl1Value.Foreground = accent;
        Pl2Value.Text = $"{limits.Pl2Watts} W"; Pl2Value.Foreground = accent;
        Pl4Value.Text = $"{limits.Pl4Watts} W"; Pl4Value.Foreground = accent;
    }

    // ---- Advanced sliders ----
    private void LoadSliders(PowerLimits limits)
    {
        if (Pl1Slider is null) return;
        Pl1Slider.Value = limits.Pl1Watts;
        Pl2Slider.Value = limits.Pl2Watts;
        Pl4Slider.Value = limits.Pl4Watts;
        UpdateSliderReadouts();
    }

    private void OnLimitChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Slider raises ValueChanged as its Maximum is set during
        // InitializeComponent — before sibling sliders/readouts exist. Bail until
        // the whole set is up (all three sliders AND their readouts).
        if (Pl1Slider is null || Pl2Slider is null || Pl4Slider is null
            || Pl1Slider_Value is null || Pl2Slider_Value is null || Pl4Slider_Value is null)
            return;

        UpdateSliderReadouts();
        if (_loading.Active) return;

        // Manual edit feeds the live envelope, then writes through on settle
        // (debounced) — no Apply button.
        ShowEnvelope(CurrentMode(), CurrentLimits());
        Toaster.Clear();
        _limitWrite.Trigger(ApplyLimitsNow);
    }

    private async void ApplyLimitsNow()
        => await Toaster.Apply(Loc.T("Power.Limits"), Loc.T("Power.LimitsSet"),
            () => _power.SetLimitsAsync(CurrentLimits()));

    private void UpdateSliderReadouts()
    {
        Pl1Slider_Value.Text = $"{(int)Pl1Slider.Value} W";
        Pl2Slider_Value.Text = $"{(int)Pl2Slider.Value} W";
        Pl4Slider_Value.Text = $"{(int)Pl4Slider.Value} W";
    }

    private void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        if (AdvancedPanel is not null)
            AdvancedPanel.Visibility = AdvancedToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Apply ----
    private PerformanceMode CurrentMode()
    {
        if (ModeGaming.IsChecked == true) return PerformanceMode.Gaming;
        if (ModeHigh.IsChecked == true) return PerformanceMode.High;
        if (ModeSaving.IsChecked == true) return PerformanceMode.Saving;
        return PerformanceMode.Balanced;
    }

    private PowerLimits CurrentLimits() =>
        new((int)Pl1Slider.Value, (int)Pl2Slider.Value, (int)Pl4Slider.Value);
}
