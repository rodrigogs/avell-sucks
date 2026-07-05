using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GamingCenter.Core.Rgb;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Views;

public partial class RgbView : UserControl
{
    private readonly IRgbService _rgb = new LocalRgbService();
    private RgbEffectType _effect = RgbEffectType.Static;
    private RgbSpeed _speed = RgbSpeed.Normal;

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
    }

    private void OnColor(Color c)
    {
        Swatch.Background = new SolidColorBrush(c);
        HexInput.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        Keyboard.Color = c;
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
        if (Badge is not null) Badge.State = WriteState.Idle;
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
        if (Badge is not null) Badge.State = WriteState.Idle;
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Keyboard is not null) Keyboard.Brightness = e.NewValue / 100.0;
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

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var c = Picker.SelectedColor;
        var effect = new RgbEffect(
            _effect,
            RgbZoneKind.FullKeyboard,
            new List<RgbColor> { new(c.R, c.G, c.B) },
            _speed,
            RgbDirection.LeftToRight,
            (byte)Math.Round(BrightnessSlider.Value));

        Badge.State = WriteState.Pending;
        Badge.Message = "";
        var result = await _rgb.ApplyAsync(effect);
        Badge.State = result.State;
        Badge.Message = result.Error ?? "";
    }
}
