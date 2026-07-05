using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Owner-drawn radial gauge: a track arc plus a neon value arc on the
/// temperature ramp, with a large mono value in the center. The value arc's
/// color and glow track how "hot"/loaded the reading is.
/// </summary>
public sealed class RadialGauge : FrameworkElement
{
    // Sweep geometry: a 270° arc opening at the bottom.
    private const double StartAngle = 135;   // degrees, 0 = +X axis, CCW positive in math but we draw CW
    private const double SweepAngle = 270;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimatedTargetChanged));

    public static readonly DependencyProperty MinProperty = DependencyProperty.Register(
        nameof(Min), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
        nameof(Max), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(RadialGauge),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(RadialGauge),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(RadialGauge),
        new FrameworkPropertyMetadata("0", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>When false, the arc uses a single accent color instead of the temperature ramp.</summary>
    public static readonly DependencyProperty UseTemperatureRampProperty = DependencyProperty.Register(
        nameof(UseTemperatureRamp), typeof(bool), typeof(RadialGauge),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Accent color used when the ramp is off.</summary>
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(RadialGauge),
        new FrameworkPropertyMetadata(Color.FromRgb(0x22, 0xD3, 0xEE), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Rendered value — animated toward Value for a smooth sweep.</summary>
    private static readonly DependencyProperty RenderValueProperty = DependencyProperty.Register(
        nameof(RenderValue), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
    public bool UseTemperatureRamp { get => (bool)GetValue(UseTemperatureRampProperty); set => SetValue(UseTemperatureRampProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    private double RenderValue { get => (double)GetValue(RenderValueProperty); set => SetValue(RenderValueProperty, value); }

    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x25, 0x1D, 0x2E));
    private static readonly Brush InkBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xEE, 0xF6));
    private static readonly Brush Ink3Brush = new SolidColorBrush(Color.FromRgb(0x94, 0x8A, 0xA3));
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface UiFace = new(new FontFamily("Segoe UI, sans-serif"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    static RadialGauge()
    {
        TrackBrush.Freeze();
        InkBrush.Freeze();
        Ink3Brush.Freeze();
    }

    private static void OnAnimatedTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (RadialGauge)d;
        var target = (double)e.NewValue;
        if (MotionPrefs.ReducedMotion)
        {
            g.RenderValue = target;
            return;
        }
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new System.Windows.Media.Animation.QuarticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        g.BeginAnimation(RenderValueProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double size = Math.Min(w, h);
        var center = new Point(w / 2, h / 2);
        double thickness = Math.Max(6, size * 0.085);
        double radius = size / 2 - thickness / 2 - 2;
        if (radius <= 0) return;

        double range = Max - Min;
        double norm = range <= 0 ? 0 : Math.Clamp((RenderValue - Min) / range, 0, 1);

        // Track arc (full sweep).
        DrawArc(dc, center, radius, StartAngle, SweepAngle, new Pen(TrackBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });

        // Value arc.
        Color valueColor = UseTemperatureRamp ? RampColor(norm) : Accent;
        if (norm > 0.001)
        {
            var glow = new SolidColorBrush(valueColor);
            glow.Freeze();
            var pen = new Pen(glow, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            // Glow intensity rises with load.
            var effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = valueColor,
                BlurRadius = 12 + norm * 16,
                ShadowDepth = 0,
                Opacity = MotionPrefs.ReducedMotion ? 0.35 : (0.35 + norm * 0.4)
            };
            // Effects can't be applied per-draw-call; approximate glow with a second, softer wide arc.
            if (!MotionPrefs.ReducedMotion)
            {
                var glowPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(70 + norm * 90), valueColor.R, valueColor.G, valueColor.B)), thickness + 6)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                DrawArc(dc, center, radius, StartAngle, SweepAngle * norm, glowPen);
            }
            DrawArc(dc, center, radius, StartAngle, SweepAngle * norm, pen);
            _ = effect;
        }

        // Center value text.
        string valueText = RenderValue.ToString(ValueFormat, CultureInfo.InvariantCulture);
        var valueFt = new FormattedText(valueText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            MonoFace, size * 0.24, InkBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(valueFt, new Point(center.X - valueFt.Width / 2, center.Y - valueFt.Height * 0.62));

        // Unit.
        if (!string.IsNullOrEmpty(Unit))
        {
            var unitFt = new FormattedText(Unit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                UiFace, size * 0.085, new SolidColorBrush(valueColor), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(unitFt, new Point(center.X - unitFt.Width / 2, center.Y + valueFt.Height * 0.22));
        }

        // Label under the value.
        if (!string.IsNullOrEmpty(Label))
        {
            var labelFt = new FormattedText(Label.ToUpperInvariantSafe(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                UiFace, size * 0.075, Ink3Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(labelFt, new Point(center.X - labelFt.Width / 2, center.Y + valueFt.Height * 0.62));
        }
    }

    /// <summary>Draws an arc clockwise from <paramref name="startDeg"/> spanning <paramref name="sweepDeg"/>.</summary>
    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;
        // WPF screen coords: Y grows downward. We draw clockwise on screen.
        Point start = PointOnCircle(center, radius, startDeg);
        Point end = PointOnCircle(center, radius, startDeg + sweepDeg);
        bool isLarge = sweepDeg > 180;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLarge,
                SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    /// <summary>Samples the temperature ramp (cyan → green → amber → magenta → red).</summary>
    private static Color RampColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        (double stop, Color color)[] stops =
        {
            (0.00, Color.FromRgb(0x22, 0xD3, 0xEE)),
            (0.35, Color.FromRgb(0x34, 0xE5, 0xA0)),
            (0.60, Color.FromRgb(0xF4, 0xC0, 0x4A)),
            (0.82, Color.FromRgb(0xFF, 0x2E, 0x88)),
            (1.00, Color.FromRgb(0xF5, 0x48, 0x4A)),
        };
        for (int i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].stop)
            {
                var (s0, c0) = stops[i - 1];
                var (s1, c1) = stops[i];
                double f = (t - s0) / (s1 - s0);
                return Lerp(c0, c1, f);
            }
        }
        return stops[^1].color;
    }

    private static Color Lerp(Color a, Color b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * f),
            (byte)(a.G + (b.G - a.G) * f),
            (byte)(a.B + (b.B - a.B) * f));
    }
}

internal static class StringCaseExtensions
{
    public static string ToUpperInvariantSafe(this string s) => s?.ToUpperInvariant() ?? string.Empty;
}
