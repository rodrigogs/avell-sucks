using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GamingCenter.UI.Controls;

/// <summary>
/// The dashboard hero: a radial LOAD gauge (arc, brand gradient, never tinted
/// by severity — 100% load is healthy) with the TEMPERATURE readout in the
/// hollow center, whose color = thermal band and whose glow blooms with heat.
/// Two facts, one focal object, zero redundancy (research spec §2).
/// </summary>
public sealed class LoadTempGauge : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(
        nameof(Load), typeof(double), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLoadChanged));

    public static readonly DependencyProperty TempCProperty = DependencyProperty.Register(
        nameof(TempC), typeof(double?), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ThermalKind), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(ThermalKind.Cpu, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty RenderLoadProperty = DependencyProperty.Register(
        nameof(RenderLoad), typeof(double), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Load { get => (double)GetValue(LoadProperty); set => SetValue(LoadProperty, value); }
    /// <summary>Temperature in °C, or null when the platform doesn't expose it.</summary>
    public double? TempC { get => (double?)GetValue(TempCProperty); set => SetValue(TempCProperty, value); }
    public ThermalKind Kind { get => (ThermalKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private double RenderLoad { get => (double)GetValue(RenderLoadProperty); set => SetValue(RenderLoadProperty, value); }

    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x24, 0x10, 0x41));
    private static readonly Brush Ink3 = Frozen(Color.FromRgb(0x7C, 0x6A, 0xA6));
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface UiFace = new(new FontFamily("Segoe UI, sans-serif"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static void OnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (LoadTempGauge)d;
        double target = Math.Clamp((double)e.NewValue, 0, 100);
        if (MotionPrefs.ReducedMotion) { g.RenderLoad = target; return; }
        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        g.BeginAnimation(RenderLoadProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double size = Math.Min(w, h);
        var center = new Point(w / 2, h / 2);
        double thickness = Math.Max(7, size * 0.09);
        double radius = size / 2 - thickness / 2 - 2;
        if (radius <= 0) return;

        double load = Math.Clamp(RenderLoad, 0, 100) / 100.0;

        // Track.
        DrawArc(dc, center, radius, StartAngle, SweepAngle, new Pen(TrackBrush, thickness)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        // Load arc — brand gradient (magenta→cyan), never severity-tinted.
        if (load > 0.001)
        {
            var grad = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0x2E, 0x97), Color.FromRgb(0x22, 0xD3, 0xEE),
                new Point(0, 0), new Point(1, 1));
            grad.Freeze();
            var pen = new Pen(grad, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            DrawArc(dc, center, radius, StartAngle, SweepAngle * load, pen);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Center: temperature readout with heat-blooming glow, OR a load %
        // fallback when temp isn't available on this platform.
        if (TempC is double t)
        {
            var band = Thermal.BandFor(t, Kind);
            var color = Thermal.ColorFor(band);
            var brush = new SolidColorBrush(color); brush.Freeze();

            var tempFt = new FormattedText($"{t:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                MonoFace, size * 0.26, brush, dpi);
            var pt = new Point(center.X - tempFt.Width / 2, center.Y - tempFt.Height * 0.62);

            // Heat bloom: draw the glyph a few times with increasing blur-like
            // offset alpha (OnRender can't apply Effect, so fake bloom via layered draws).
            if (!MotionPrefs.ReducedMotion)
            {
                double heat = Thermal.Heat01(t);
                byte a = (byte)(40 + heat * 90);
                var glow = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B)); glow.Freeze();
                var glowFt = new FormattedText($"{t:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    MonoFace, size * 0.26 * (1.0 + 0.04 + heat * 0.05), glow, dpi);
                dc.DrawText(glowFt, new Point(center.X - glowFt.Width / 2, center.Y - glowFt.Height * 0.62));
            }
            dc.DrawText(tempFt, pt);

            var lblFt = new FormattedText($"{Load:0}% load", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                UiFace, size * 0.075, Ink3, dpi);
            dc.DrawText(lblFt, new Point(center.X - lblFt.Width / 2, center.Y + tempFt.Height * 0.30));
        }
        else
        {
            // No temp sensor — center shows the load number instead.
            var loadColor = new SolidColorBrush(Color.FromRgb(0xF3, 0xEC, 0xFF)); loadColor.Freeze();
            var loadFt = new FormattedText($"{RenderLoad:0}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                MonoFace, size * 0.26, loadColor, dpi);
            dc.DrawText(loadFt, new Point(center.X - loadFt.Width / 2, center.Y - loadFt.Height * 0.62));
            var unitFt = new FormattedText("% load", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                UiFace, size * 0.08, new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)), dpi);
            dc.DrawText(unitFt, new Point(center.X - unitFt.Width / 2, center.Y + loadFt.Height * 0.22));
            var noTemp = new FormattedText("temp n/a", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                UiFace, size * 0.06, Ink3, dpi);
            dc.DrawText(noTemp, new Point(center.X - noTemp.Width / 2, center.Y + loadFt.Height * 0.62));
        }
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;
        Point start = OnCircle(c, r, startDeg);
        Point end = OnCircle(c, r, startDeg + sweepDeg);
        bool large = sweepDeg > 180;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, large, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point OnCircle(Point c, double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
