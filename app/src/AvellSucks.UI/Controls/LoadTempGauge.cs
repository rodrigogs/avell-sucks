using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AvellSucks.UI.Localization;

namespace AvellSucks.UI.Controls;

/// <summary>
/// The dashboard hero: a radial LOAD gauge (arc, brand gradient, never tinted
/// by severity) with the TEMPERATURE readout in the hollow center, whose color
/// = thermal band and whose glow blooms with heat via a real DropShadowEffect
/// (BlurRadius 8→28, Opacity .5→.9). Critical band adds a slow pulse. When the
/// platform exposes no temperature, the center shows load % + "temp n/a".
/// (Research spec §1–2.)
/// </summary>
public sealed class LoadTempGauge : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    // Child visuals hosted for crisp text + a real glow effect.
    private readonly TextBlock _big;      // temp value, or load number when no temp
    private readonly TextBlock _sub;      // "NN% load" / "% load"
    private readonly TextBlock _tiny;     // "temp n/a" hint (no-temp mode only)
    private readonly StackPanel _stack;
    private readonly DropShadowEffect _glow;
    private readonly VisualCollection _children;

    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(
        nameof(Load), typeof(double), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLoadChanged));

    public static readonly DependencyProperty TempCProperty = DependencyProperty.Register(
        nameof(TempC), typeof(double?), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ThermalKind), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(ThermalKind.Cpu, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

    private static readonly DependencyProperty RenderLoadProperty = DependencyProperty.Register(
        nameof(RenderLoad), typeof(double), typeof(LoadTempGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

    public double Load { get => (double)GetValue(LoadProperty); set => SetValue(LoadProperty, value); }
    public double? TempC { get => (double?)GetValue(TempCProperty); set => SetValue(TempCProperty, value); }
    public ThermalKind Kind { get => (ThermalKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private double RenderLoad { get => (double)GetValue(RenderLoadProperty); set => SetValue(RenderLoadProperty, value); }

    private static readonly Brush TrackBrush = Brand.Frozen(Color.FromRgb(0x24, 0x10, 0x41));
    private static readonly Brush Ink3 = Brand.Frozen(Color.FromRgb(0x7C, 0x6A, 0xA6));
    private static readonly FontFamily MonoFamily = new("Cascadia Code, Consolas, monospace");
    private static readonly FontFamily UiFamily = new("Segoe UI, sans-serif");

    public LoadTempGauge()
    {
        _glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 12, Opacity = 0.6, Color = Thermal.Cold };
        _big = new TextBlock
        {
            FontFamily = MonoFamily, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Effect = _glow
        };
        _sub = new TextBlock
        {
            FontFamily = UiFamily, Foreground = Ink3,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _tiny = new TextBlock
        {
            FontFamily = UiFamily, Foreground = Ink3, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _stack.Children.Add(_big);
        _stack.Children.Add(_sub);
        _stack.Children.Add(_tiny);
        _children = new VisualCollection(this) { _stack };
    }

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        _stack.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateContent(finalSize);
        _stack.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void OnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (LoadTempGauge)d;
        double target = Math.Clamp((double)e.NewValue, 0, 100);
        if (MotionPrefs.ReducedMotion) { g.RenderLoad = target; return; }
        g.BeginAnimation(RenderLoadProperty, new DoubleAnimation
        {
            To = target, Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LoadTempGauge)d).UpdateContent(new Size(((LoadTempGauge)d).ActualWidth, ((LoadTempGauge)d).ActualHeight));

    private ThermalBand _lastBand = (ThermalBand)(-1);

    private void UpdateContent(Size size)
    {
        double s = Math.Min(size.Width, size.Height);
        if (s <= 0) return;

        if (TempC is double t)
        {
            var band = Thermal.BandFor(t, Kind);
            var color = Thermal.ColorFor(band);
            _big.Text = $"{t:0}°";
            _big.FontSize = s * 0.27;
            _big.Foreground = new SolidColorBrush(color);
            _glow.Color = color;
            _glow.BlurRadius = Thermal.GlowBlur(t);
            _glow.Opacity = Thermal.GlowOpacity(t);
            _sub.Text = $"{RenderLoad:0}{Loc.T("Gauge.LoadSuffix")}";
            _sub.FontSize = s * 0.08;
            _tiny.Visibility = Visibility.Collapsed;

            // Critical band pulses; other bands are static.
            if (band != _lastBand)
            {
                _lastBand = band;
                if (band == ThermalBand.Critical && !MotionPrefs.ReducedMotion)
                    _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation
                    {
                        From = 0.5, To = 0.95, Duration = TimeSpan.FromSeconds(1.1),
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
                    });
                else
                    _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            }
        }
        else
        {
            _big.Text = $"{RenderLoad:0}";
            _big.FontSize = s * 0.27;
            _big.Foreground = Brand.Frozen(Color.FromRgb(0xF3, 0xEC, 0xFF));
            _glow.Color = Thermal.Cold;
            _glow.BlurRadius = 8;
            _glow.Opacity = 0.35;
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            _sub.Text = Loc.T("Gauge.LoadLabel");
            _sub.FontSize = s * 0.085;
            _tiny.Text = Loc.T("Gauge.TempNa");
            _tiny.FontSize = s * 0.06;
            _tiny.Visibility = Visibility.Visible;
            _lastBand = (ThermalBand)(-1);
        }
    }

    // Frozen once — the value-arc gradient is a constant magenta→cyan.
    private static readonly LinearGradientBrush s_arcGrad = CreateArcGrad();
    private static LinearGradientBrush CreateArcGrad()
    {
        var g = new LinearGradientBrush(Brand.Magenta, Brand.Cyan, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    // Pens depend only on stroke thickness (changes on resize, not per frame).
    // Cache and rebuild only when thickness changes — OnRender ran ~60fps during
    // the 220ms load animation and allocated two Pens + a brush every frame.
    private double _penThickness = -1;
    private Pen? _trackPen;
    private Pen? _arcPen;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double size = Math.Min(w, h);
        var center = new Point(w / 2, h / 2);
        double thickness = Math.Max(7, size * 0.09);
        double radius = size / 2 - thickness / 2 - 2;
        if (radius <= 0) return;

        if (thickness != _penThickness || _trackPen is null || _arcPen is null)
        {
            _penThickness = thickness;
            _trackPen = new Pen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _trackPen.Freeze();
            _arcPen = new Pen(s_arcGrad, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _arcPen.Freeze();
        }

        double load = Math.Clamp(RenderLoad, 0, 100) / 100.0;

        DrawArc(dc, center, radius, StartAngle, SweepAngle, _trackPen);
        if (load > 0.001)
            DrawArc(dc, center, radius, StartAngle, SweepAngle * load, _arcPen);

        // Keep the hosted readout in sync each render (RenderLoad animates).
        UpdateContent(new Size(w, h));
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
