using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AvellSucks.UI.Controls;

/// <summary>
/// Compact radial LOAD-only gauge (no temperature) used for the integrated GPU
/// beside the full dGPU gauge. Draws a 270° arc in the GPU identity cyan with the
/// load % in the center. The child TextBlock carries the value into UI Automation
/// (reads as "76%"); the adjacent "iGPU" caption is the human label.
/// </summary>
public sealed class MiniLoadGauge : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(
        nameof(Load), typeof(double), typeof(MiniLoadGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Load { get => (double)GetValue(LoadProperty); set => SetValue(LoadProperty, value); }

    private readonly TextBlock _text;
    private readonly VisualCollection _children;

    private static readonly Brush TrackBrush = Brand.Frozen(Brand.Track);
    private static readonly Brush ArcBrush = Brand.Frozen(Brand.Cyan);

    public MiniLoadGauge()
    {
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brand.Frozen(Color.FromRgb(0xF3, 0xEC, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _children = new VisualCollection(this) { _text };
    }

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateContent(finalSize);
        _text.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void UpdateContent(Size size)
    {
        double s = Math.Min(size.Width, size.Height);
        if (s <= 0) return;
        _text.Text = $"{Math.Clamp(Load, 0, 100):0}%";
        _text.FontSize = s * 0.26;
    }

    // Pens depend only on stroke thickness; rebuilt on resize, never per frame.
    private double _penThickness = -1;
    private Pen? _trackPen;
    private Pen? _arcPen;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double size = Math.Min(w, h);
        var center = new Point(w / 2, h / 2);
        double thickness = Math.Max(6, size * 0.10);
        double radius = size / 2 - thickness / 2 - 1;
        if (radius <= 0) return;

        if (thickness != _penThickness || _trackPen is null || _arcPen is null)
        {
            _penThickness = thickness;
            _trackPen = new Pen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _trackPen.Freeze();
            _arcPen = new Pen(ArcBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _arcPen.Freeze();
        }

        double load = Math.Clamp(Load, 0, 100) / 100.0;
        DrawArc(dc, center, radius, StartAngle, SweepAngle, _trackPen);
        if (load > 0.001)
            DrawArc(dc, center, radius, StartAngle, SweepAngle * load, _arcPen);

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
