using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GamingCenter.Core.Rgb;

namespace GamingCenter.UI.Controls;

/// <summary>
/// A stylized keyboard that renders the currently-selected RGB effect on its
/// keycaps, so the user sees the real effect before applying. Animates
/// breathing / cycle / wave / ripple; static is a steady fill.
/// </summary>
public sealed class KeyboardPreview : FrameworkElement
{
    // Rough per-row key counts for a laptop layout — enough to read as a keyboard.
    private static readonly int[] Rows = { 14, 14, 13, 12, 8 };

    public static readonly DependencyProperty EffectTypeProperty = DependencyProperty.Register(
        nameof(EffectType), typeof(RgbEffectType), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(RgbEffectType.Static, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0x2E, 0x88), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
        nameof(Brightness), typeof(double), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Animation clock 0..1, driven internally.</summary>
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public RgbEffectType EffectType
    {
        get => (RgbEffectType)GetValue(EffectTypeProperty);
        set { SetValue(EffectTypeProperty, value); RestartAnimation(); }
    }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    private double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    public KeyboardPreview()
    {
        Loaded += (_, _) => RestartAnimation();
        Unloaded += (_, _) => BeginAnimation(PhaseProperty, null);
    }

    private void RestartAnimation()
    {
        BeginAnimation(PhaseProperty, null);
        if (MotionPrefs.ReducedMotion || EffectType == RgbEffectType.Static) { Phase = 0; return; }

        double seconds = EffectType switch
        {
            RgbEffectType.Breathing => 3.0,
            RgbEffectType.ColorCycle => 6.0,
            RgbEffectType.Wave => 2.6,
            RgbEffectType.Ripple => 2.2,
            _ => 3.0
        };
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        BeginAnimation(PhaseProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Board backing.
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x18)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x2F, 0x47)), 1),
            new Rect(0, 0, w, h), 10, 10);

        double pad = 14;
        double gap = 5;
        double innerW = w - pad * 2;
        double innerH = h - pad * 2;
        int rowCount = Rows.Length;
        double keyH = (innerH - gap * (rowCount - 1)) / rowCount;

        double phase = Phase;

        for (int r = 0; r < rowCount; r++)
        {
            int keys = Rows[r];
            double keyW = (innerW - gap * (keys - 1)) / keys;
            double y = pad + r * (keyH + gap);
            for (int c = 0; c < keys; c++)
            {
                double x = pad + c * (keyW + gap);
                double nx = keys > 1 ? c / (double)(keys - 1) : 0; // 0..1 across
                double ny = rowCount > 1 ? r / (double)(rowCount - 1) : 0;

                Color key = KeyColor(nx, ny, phase);
                var brush = new SolidColorBrush(key);
                dc.DrawRoundedRectangle(brush, null, new Rect(x, y, keyW, keyH), 3, 3);
            }
        }
    }

    private Color KeyColor(double nx, double ny, double phase)
    {
        double b = Math.Clamp(Brightness, 0, 1);
        switch (EffectType)
        {
            case RgbEffectType.Static:
                return Scale(Color, b);

            case RgbEffectType.Breathing:
            {
                double k = 0.25 + 0.75 * (0.5 - 0.5 * Math.Cos(phase * 2 * Math.PI));
                return Scale(Color, b * k);
            }

            case RgbEffectType.ColorCycle:
            {
                double hue = (phase + nx * 0.15) % 1.0 * 360.0;
                return Scale(HsvPicker.FromHsv(hue, 0.9, 1.0), b);
            }

            case RgbEffectType.Wave:
            {
                double hue = ((nx - phase) % 1.0 + 1.0) % 1.0 * 360.0;
                return Scale(HsvPicker.FromHsv(hue, 0.9, 1.0), b);
            }

            case RgbEffectType.Ripple:
            {
                double dx = nx - 0.5, dy = ny - 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy) * 1.6;
                double k = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin((dist - phase) * 2 * Math.PI * 2));
                return Scale(Color, b * k);
            }

            default:
                return Scale(Color, b);
        }
    }

    private static Color Scale(Color c, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }

}
