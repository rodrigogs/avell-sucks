using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingCenter.UI.Controls;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class PowerView : UserControl
{
    private readonly IPowerService _power = new LocalPowerService();
    private bool _loading;
    private bool _advancedDirty; // true once the user edits a slider away from the preset

    public PowerView()
    {
        InitializeComponent();
        BuildModeCards();
        Loaded += OnLoaded;
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
    // legible before selecting. Accent dot tracks intensity.
    private void BuildModeCards()
    {
        foreach (var m in new[] { PerformanceMode.Gaming, PerformanceMode.High, PerformanceMode.Balanced, PerformanceMode.Saving })
        {
            var meta = Meta(m);
            var preset = LocalPowerService.PresetFor(m);
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
    }

    // ---- Mode selection ----
    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var mode = ModeOf(rb);
        var preset = LocalPowerService.PresetFor(mode);

        _advancedDirty = false;
        ShowEnvelope(mode, preset);

        // Checked can fire during InitializeComponent before later elements exist.
        if (Pl1Slider is not null)
        {
            _loading = true;
            LoadSliders(preset);
            _loading = false;
        }

        if (!_loading && Badge is not null) Badge.State = WriteState.Idle;
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
        if (Pl1Slider_Value is null) return; // fires during InitializeComponent
        UpdateSliderReadouts();
        if (_loading) return;

        // Manual edit detaches from the preset and feeds the live envelope.
        _advancedDirty = true;
        ShowEnvelope(CurrentMode(), CurrentLimits());
        if (Badge is not null) Badge.State = WriteState.Idle;
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

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        Badge.State = WriteState.Pending;
        Badge.Message = "";

        // Manual limits win when the user tuned them; otherwise apply the mode
        // package (Windows scheme + preset).
        var result = _advancedDirty
            ? await _power.SetLimitsAsync(CurrentLimits())
            : await _power.SetModeAsync(CurrentMode());

        Badge.State = result.State;
        Badge.Message = result.Error ?? "";
    }
}
