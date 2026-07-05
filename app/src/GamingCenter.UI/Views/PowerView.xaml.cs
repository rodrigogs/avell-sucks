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

    // Brand tokens (single source in Brand), matching Palette Cyan/Danger.
    private static readonly Brush CyanBrush = Brand.Frozen(Brand.Cyan);
    private static readonly Brush DangerBrush = Brand.Frozen(Brand.Danger);

    public PowerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        if (!_power.WritesEnabled)
            GateNotice.Visibility = Visibility.Visible;

        var r = await _power.GetAsync();
        Pl1Slider.Value = r.Pl1Watts;
        Pl2Slider.Value = r.Pl2Watts;
        TauSlider.Value = r.TauSeconds;
        _loading = false;
        UpdateReadouts();
    }

    private void OnAnyChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // ValueChanged fires during InitializeComponent (inline Value=) before
        // later-declared elements exist — bail until the tree is up.
        if (Pl1Value is null || Badge is null) return;
        UpdateReadouts();
        if (!_loading) Badge.State = WriteState.Idle;
    }

    private void UpdateReadouts()
    {
        int pl1 = (int)Pl1Slider.Value;
        int pl2 = (int)Pl2Slider.Value;
        int tau = (int)TauSlider.Value;

        Pl1Value.Text = $"{pl1} W";
        Pl2Value.Text = $"{pl2} W";
        TauValue.Text = $"{tau} s";

        // Danger legibility: high sustained/turbo draw tints the readout red.
        Pl1Value.Foreground = pl1 >= 70 ? DangerBrush : CyanBrush;
        Pl2Value.Foreground = pl2 >= 95 ? DangerBrush : CyanBrush;
        // Keep PL2 >= PL1 sane hint via color only (non-blocking).
        if (pl2 < pl1) Pl2Value.Foreground = DangerBrush;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        Badge.State = WriteState.Pending;
        Badge.Message = "";
        var result = await _power.SetAsync((int)Pl1Slider.Value, (int)Pl2Slider.Value, (int)TauSlider.Value);
        Badge.State = result.State;
        Badge.Message = result.Error ?? "";
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var r = await _power.GetAsync();
        _loading = true;
        Pl1Slider.Value = r.Pl1Watts;
        Pl2Slider.Value = r.Pl2Watts;
        TauSlider.Value = r.TauSeconds;
        _loading = false;
        UpdateReadouts();
        Badge.State = WriteState.Idle;
    }
}
