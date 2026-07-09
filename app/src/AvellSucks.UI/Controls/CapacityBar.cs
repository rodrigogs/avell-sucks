using System;
using System.Windows;
using System.Windows.Media;

namespace AvellSucks.UI.Controls;

/// <summary>
/// A horizontal capacity meter. Fill = brand gradient (cyan→magenta) while
/// healthy, recoloring amber then red past the warn/critical fractions
/// (research spec Rule B). Optionally shows a distinct "secondary" segment
/// (e.g. RAM cache/standby, or VRAM shared spillover) and a threshold tick.
/// </summary>
public sealed class CapacityBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Secondary segment fraction drawn after the primary (cache / shared). 0 = none.</summary>
    public static readonly DependencyProperty SecondaryFractionProperty = DependencyProperty.Register(
        nameof(SecondaryFraction), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarnAtProperty = DependencyProperty.Register(
        nameof(WarnAt), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(0.80, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalAtProperty = DependencyProperty.Register(
        nameof(CriticalAt), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(0.95, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Optional faint tick at this fraction (e.g. commit 0.90 pagefile-grow). NaN = none.</summary>
    public static readonly DependencyProperty TickAtProperty = DependencyProperty.Register(
        nameof(TickAt), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>When true, the secondary segment is a spillover warning (amber/red), not muted cache.</summary>
    public static readonly DependencyProperty SecondaryIsSpilloverProperty = DependencyProperty.Register(
        nameof(SecondaryIsSpillover), typeof(bool), typeof(CapacityBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double SecondaryFraction { get => (double)GetValue(SecondaryFractionProperty); set => SetValue(SecondaryFractionProperty, value); }
    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    public double CriticalAt { get => (double)GetValue(CriticalAtProperty); set => SetValue(CriticalAtProperty, value); }
    public double TickAt { get => (double)GetValue(TickAtProperty); set => SetValue(TickAtProperty, value); }
    public bool SecondaryIsSpillover { get => (bool)GetValue(SecondaryIsSpilloverProperty); set => SetValue(SecondaryIsSpilloverProperty, value); }

    private static readonly Brush Track = Brand.Frozen(Brand.Track);
    private static readonly Brush Cache = Brand.Frozen(Color.FromRgb(0x6D, 0x4A, 0xA0)); // muted, reclaimable

    // Healthy fill + threshold tick never change, so build them once instead of
    // per render (matches LoadTempGauge's cached-gradient/-pen pattern).
    private static readonly Brush HealthyFill = FrozenGradient(Brand.Cyan, Brand.Magenta);
    private static readonly Pen TickPen = FrozenPen(Color.FromArgb(0xAA, 0xF3, 0xEC, 0xFF), 1.5);

    private static Brush FrozenGradient(Color a, Color b)
    {
        var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 0));
        g.Freeze();
        return g;
    }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(Brand.Frozen(c), thickness);
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = h / 2;

        // Track.
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);

        double frac = Math.Clamp(Fraction, 0, 1);
        double primW = w * frac;

        // Primary fill color per severity.
        Brush fill;
        if (frac >= CriticalAt) fill = Brand.Frozen(Thermal.Critical);
        else if (frac >= WarnAt) fill = Brand.Frozen(Thermal.Warm);
        else fill = HealthyFill;

        if (primW > 0.5)
        {
            var clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            clip.Freeze();
            dc.PushClip(clip);
            dc.DrawRectangle(fill, null, new Rect(0, 0, primW, h));

            // Secondary segment right after the primary.
            double sec = Math.Clamp(SecondaryFraction, 0, 1 - frac);
            if (sec > 0.001)
            {
                Brush secBrush = SecondaryIsSpillover
                    ? Brand.Frozen((frac + sec) >= CriticalAt ? Thermal.Critical : Thermal.Warm)
                    : Cache;
                dc.DrawRectangle(secBrush, null, new Rect(primW, 0, w * sec, h));
            }
            dc.Pop();
        }

        // Threshold tick.
        if (!double.IsNaN(TickAt))
        {
            double tx = w * Math.Clamp(TickAt, 0, 1);
            dc.DrawLine(TickPen, new Point(tx, 1), new Point(tx, h - 1));
        }
    }
}
