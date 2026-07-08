using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingCenter.UI.Controls;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

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
    private bool _loading = true;

    public PowerView()
    {
        InitializeComponent();
        BuildModeCards();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        if (HardwareServices.IsRealBackendActive)
        {
            _monitor = new PowerStateMonitor();
            _monitor.ExternalModeChanged += OnExternalModeChanged;
        }
    }

    // ---- Mode metadata (name, one-line meaning, accent by intensity) ----
    private readonly record struct ModeMeta(string Name, string Tagline, Color Accent);

    private static ModeMeta Meta(PerformanceMode m) => m switch
    {
        PerformanceMode.Gaming   => new("Gaming", "Max performance, fans free", Brand.Magenta),
        PerformanceMode.High     => new("High", "Strong, still balanced", Brand.Violet),
        PerformanceMode.Balanced => new("Balanced", "Everyday default", Brand.Cyan),
        PerformanceMode.Saving   => new("Saving", "Cool, quiet, long battery", Brand.Ok),
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
                Text = "PL1 / PL2",
                Style = (Style)Application.Current.FindResource("Caption"),
            });

            CardFor(m).Content = panel;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_power.WritesEnabled)
            GateNotice.Visibility = Visibility.Visible;

        var state = await _power.GetAsync();

        _loading = true;
        CardFor(state.Mode).IsChecked = true;
        ShowEnvelope(state.Mode, state.Limits);
        LoadSliders(state.Limits);
        _loading = false;

        _monitor?.NoteLocalWrite(state.Mode); // seed baseline with current plan
        _monitor?.Start();
        App.Trace($"PowerView loaded: monitor={( _monitor is null ? "NULL(stub)" : "active")} mode={state.Mode}");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _monitor?.Stop();

    // Active Windows power plan changed outside our app: reflect it without a
    // self-authored write toast.
    private async void OnExternalModeChanged(PerformanceMode mode)
    {
        App.Trace($"PowerStateMonitor: external power-plan change detected → {mode}");
        _loading = true;
        CardFor(mode).IsChecked = true;
        var preset = await _power.GetPresetAsync(mode);
        ShowEnvelope(mode, preset);
        LoadSliders(preset);
        _loading = false;
        Toaster.Show(WriteState.Verified, Meta(mode).Name + " mode · changed on device");
    }

    // ---- Mode selection: actuates immediately (no Apply button) ----
    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var mode = ModeOf(rb);
        var preset = await _power.GetPresetAsync(mode);

        ShowEnvelope(mode, preset);

        // Checked can fire during InitializeComponent before later elements exist.
        if (Pl1Slider is not null)
        {
            _loading = true;
            LoadSliders(preset);
            _loading = false;
        }

        if (_loading) return; // initial selection during load — don't write
        _limitWrite.Cancel(); // mode press supersedes a pending slider write
        _monitor?.NoteLocalWrite(mode); // our own switch — not an external change

        var label = Meta(mode).Name + " mode";
        Toaster.Show(WriteState.Pending, label);
        var result = await _power.SetModeAsync(mode);
        Toaster.Show(result.State, label + " on", result.Error);
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
        if (_loading) return;

        // Manual edit feeds the live envelope, then writes through on settle
        // (debounced) — no Apply button.
        ShowEnvelope(CurrentMode(), CurrentLimits());
        Toaster.Clear();
        _limitWrite.Trigger(ApplyLimitsNow);
    }

    private async void ApplyLimitsNow()
    {
        Toaster.Show(WriteState.Pending, "Power limits");
        var result = await _power.SetLimitsAsync(CurrentLimits());
        Toaster.Show(result.State, "Power limits set", result.Error);
    }

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
