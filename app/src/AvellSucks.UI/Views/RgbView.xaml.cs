using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvellSucks.Core.Rgb;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Views;

public partial class RgbView : UserControl
{
    private readonly IRgbService _rgb = HardwareServices.CreateRgbService();
    private readonly Debouncer _apply = new(450);
    private RgbEffectType _effect = RgbEffectType.Static;
    private RgbSpeed _speed = RgbSpeed.Normal;
    private bool _loading = true;

    public RgbView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Picker.ColorChanged += (_, _) => OnColor(Picker.SelectedColor);
        FxStatic.IsChecked = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_rgb.Available)
            DeviceNotice.Visibility = Visibility.Visible;

        var c = Color.FromRgb(0xFF, 0x2E, 0x88);
        Picker.SetColor(c);
        OnColor(c);
        _loading = false; // initial paint done — user edits now actuate
    }

    private void OnColor(Color c)
    {
        Swatch.Background = new SolidColorBrush(c);
        HexInput.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        Keyboard.Color = c;
        QueueApply(); // live: color change re-applies lighting
    }

    private void OnEffectChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _effect = rb.Name switch
        {
            nameof(FxBreathing) => RgbEffectType.Breathing,
            nameof(FxCycle) => RgbEffectType.ColorCycle,
            nameof(FxWave) => RgbEffectType.Wave,
            nameof(FxRipple) => RgbEffectType.Ripple,
            _ => RgbEffectType.Static,
        };
        // Checked fires during InitializeComponent, before the other named
        // elements exist — guard until the tree is up.
        if (Keyboard is not null) Keyboard.EffectType = _effect;
        QueueApply();
    }

    private void OnSpeedChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _speed = rb.Name switch
        {
            nameof(SpeedSlow) => RgbSpeed.Slow,
            nameof(SpeedFast) => RgbSpeed.Fast,
            _ => RgbSpeed.Normal,
        };
        QueueApply();
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Keyboard is not null) Keyboard.Brightness = e.NewValue / 100.0;
        QueueApply();
    }

    private void OnHexKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    private void OnHexCommit(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        var text = HexInput.Text.Trim().TrimStart('#');
        if (text.Length == 6
            && byte.TryParse(text.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            var c = Color.FromRgb(r, g, b);
            Picker.SetColor(c);
            OnColor(c);
        }
        else
        {
            // Revert to the current picker color if the input is malformed.
            OnColor(Picker.SelectedColor);
        }
    }

    // Live: any lighting edit re-applies on settle (debounced). No Apply button.
    private void QueueApply()
    {
        if (_loading || BrightnessSlider is null) return;
        Toaster.Clear();
        _apply.Trigger(ApplyLightingNow);
    }

    private async void ApplyLightingNow()
    {
        var c = Picker.SelectedColor;
        var effect = new RgbEffect(
            _effect,
            RgbZoneKind.FullKeyboard,
            new List<RgbColor> { new(c.R, c.G, c.B) },
            _speed,
            RgbDirection.LeftToRight,
            (byte)Math.Round(BrightnessSlider.Value));

        Toaster.Show(WriteState.Pending, "Lighting");
        var result = await _rgb.ApplyAsync(effect);
        Toaster.Show(result.State, "Lighting applied", result.Error);
    }
}
