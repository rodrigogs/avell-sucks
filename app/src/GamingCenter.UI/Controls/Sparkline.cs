using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Live line chart of a rolling value history: a neon stroke over a soft area
/// fade, no gridline clutter. Feed it via <see cref="Push"/>.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private readonly List<double> _values = new();
    private int _capacity = 90;

    public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register(
        nameof(StrokeColor), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Color.FromRgb(0x22, 0xD3, 0xEE), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinProperty = DependencyProperty.Register(
        nameof(Min), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
        nameof(Max), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color StrokeColor { get => (Color)GetValue(StrokeColorProperty); set => SetValue(StrokeColorProperty, value); }

    /// <summary>Fixed lower bound; NaN = auto from data.</summary>
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }

    /// <summary>Fixed upper bound; NaN = auto from data.</summary>
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }

    public int Capacity
    {
        get => _capacity;
        set { _capacity = Math.Max(2, value); TrimAndRender(); }
    }

    /// <summary>Append a sample; oldest is dropped past capacity.</summary>
    public void Push(double value)
    {
        _values.Add(value);
        TrimAndRender();
    }

    private void TrimAndRender()
    {
        if (_values.Count > _capacity)
            _values.RemoveRange(0, _values.Count - _capacity);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _values.Count < 2) return;

        double lo = double.IsNaN(Min) ? double.MaxValue : Min;
        double hi = double.IsNaN(Max) ? double.MinValue : Max;
        if (double.IsNaN(Min) || double.IsNaN(Max))
        {
            foreach (var v in _values)
            {
                if (double.IsNaN(Min) && v < lo) lo = v;
                if (double.IsNaN(Max) && v > hi) hi = v;
            }
        }
        if (hi - lo < 1e-6) { hi = lo + 1; }
        double pad = (hi - lo) * 0.12;
        lo -= pad; hi += pad;

        double stepX = w / (_capacity - 1);
        double MapY(double v) => h - (v - lo) / (hi - lo) * h;

        var geo = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var ctx = geo.Open())
        using (var fctx = fill.Open())
        {
            // Right-align the series so the newest sample is at the right edge.
            int n = _values.Count;
            double x0 = w - (n - 1) * stepX;
            var first = new Point(x0, MapY(_values[0]));
            ctx.BeginFigure(first, false, false);
            fctx.BeginFigure(new Point(x0, h), true, true);
            fctx.LineTo(first, true, false);

            for (int i = 1; i < n; i++)
            {
                var p = new Point(x0 + i * stepX, MapY(_values[i]));
                ctx.LineTo(p, true, true);
                fctx.LineTo(p, true, false);
            }
            fctx.LineTo(new Point(x0 + (n - 1) * stepX, h), true, false);
        }
        geo.Freeze();
        fill.Freeze();

        Color c = StrokeColor;
        var area = new LinearGradientBrush(
            Color.FromArgb(0x55, c.R, c.G, c.B),
            Color.FromArgb(0x00, c.R, c.G, c.B),
            new Point(0, 0), new Point(0, 1));
        area.Freeze();
        dc.DrawGeometry(area, null, fill);

        var stroke = new Pen(new SolidColorBrush(c), 2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, stroke, geo);
    }
}
