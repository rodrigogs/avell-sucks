using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Draggable temp→PWM fan curve. Five nodes on a plotted line; drag a node
/// vertically to set its PWM. Temperatures are fixed breakpoints. Neon nodes,
/// live line, temperature-ramp fill under the curve.
/// </summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const int MaxPwm = 140; // 0x8C
    private const double NodeRadius = 7;
    private const double PadLeft = 40, PadRight = 16, PadTop = 14, PadBottom = 26;

    private List<FanPoint> _points = new()
    {
        new(50, 40), new(60, 64), new(70, 90), new(80, 116), new(90, 140)
    };
    private int _dragIndex = -1;

    /// <summary>Raised when a node is dragged, so the host can enable "apply".</summary>
    public event EventHandler? CurveChanged;

    public IReadOnlyList<FanPoint> Points => _points;

    public void SetPoints(IReadOnlyList<FanPoint> points)
    {
        if (points is { Count: 5 })
        {
            _points = points.Select(p => p).ToList();
            InvalidateVisual();
        }
    }

    private static readonly Brush TrackBrush = Brand.Frozen(Color.FromRgb(0x25, 0x1D, 0x2E));
    private static readonly Brush GridBrush = Brand.Frozen(Color.FromArgb(0x40, 0x3A, 0x2F, 0x47));
    private static readonly Brush InkBrush = Brand.Frozen(Color.FromRgb(0xF2, 0xEE, 0xF6));
    private static readonly Brush Ink3Brush = Brand.Frozen(Color.FromRgb(0x94, 0x8A, 0xA3));
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private Rect PlotRect => new(PadLeft, PadTop,
        Math.Max(1, ActualWidth - PadLeft - PadRight),
        Math.Max(1, ActualHeight - PadTop - PadBottom));

    private Point ToScreen(FanPoint p)
    {
        var r = PlotRect;
        double tx = (p.TemperatureC - 40.0) / (100.0 - 40.0); // temps span 40..100 for headroom
        double ty = p.Pwm / (double)MaxPwm;
        return new Point(r.Left + tx * r.Width, r.Bottom - ty * r.Height);
    }

    private int PwmFromY(double y)
    {
        var r = PlotRect;
        double ty = Math.Clamp((r.Bottom - y) / r.Height, 0, 1);
        return (int)Math.Round(ty * MaxPwm);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = PlotRect;
        if (r.Width <= 1 || r.Height <= 1) return;

        // Plot background + horizontal gridlines at 25/50/75/100%.
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
        for (int i = 0; i <= 4; i++)
        {
            double y = r.Bottom - i / 4.0 * r.Height;
            dc.DrawLine(new Pen(GridBrush, 1), new Point(r.Left, y), new Point(r.Right, y));
            var pctFt = new FormattedText($"{i * 25}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                MonoFace, 10, Ink3Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(pctFt, new Point(r.Left - 26, y - pctFt.Height / 2));
        }

        // Build the curve polyline.
        var pts = _points.Select(ToScreen).ToArray();

        // Area fill under the curve on the temperature ramp.
        var fill = new StreamGeometry();
        using (var fctx = fill.Open())
        {
            fctx.BeginFigure(new Point(pts[0].X, r.Bottom), true, true);
            fctx.LineTo(pts[0], true, false);
            for (int i = 1; i < pts.Length; i++) fctx.LineTo(pts[i], true, false);
            fctx.LineTo(new Point(pts[^1].X, r.Bottom), true, false);
        }
        fill.Freeze();
        var ramp = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x40, 0x22, 0xD3, 0xEE), 0.0),
                new GradientStop(Color.FromArgb(0x40, 0x34, 0xE5, 0xA0), 0.35),
                new GradientStop(Color.FromArgb(0x40, 0xF4, 0xC0, 0x4A), 0.6),
                new GradientStop(Color.FromArgb(0x55, 0xFF, 0x2E, 0x88), 0.82),
                new GradientStop(Color.FromArgb(0x55, 0xF5, 0x48, 0x4A), 1.0),
            }
        };
        ramp.Freeze();
        dc.DrawGeometry(ramp, null, fill);

        // Curve line.
        var line = new StreamGeometry();
        using (var lctx = line.Open())
        {
            lctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Length; i++) lctx.LineTo(pts[i], true, true);
        }
        line.Freeze();
        var linePen = new Pen(Brand.Frozen(Color.FromRgb(0x22, 0xD3, 0xEE)), 2.5)
        { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, linePen, line);

        // Nodes + PWM labels.
        for (int i = 0; i < pts.Length; i++)
        {
            var c = pts[i];
            bool active = i == _dragIndex;
            var nodeColor = Color.FromRgb(0xFF, 0x2E, 0x88);
            dc.DrawEllipse(Brand.Frozen(Color.FromRgb(0x14, 0x10, 0x18)), new Pen(Brand.Frozen(nodeColor), 2.5),
                c, NodeRadius + (active ? 2 : 0), NodeRadius + (active ? 2 : 0));
            dc.DrawEllipse(Brand.Frozen(nodeColor), null, c, 2.5, 2.5);

            // temp label on X axis
            var tempFt = new FormattedText($"{_points[i].TemperatureC}°", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MonoFace, 10.5, Ink3Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(tempFt, new Point(c.X - tempFt.Width / 2, r.Bottom + 6));

            // pwm value above node
            var pwmPct = (int)Math.Round(_points[i].Pwm / (double)MaxPwm * 100);
            var pwmFt = new FormattedText($"{pwmPct}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                MonoFace, 11, InkBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(pwmFt, new Point(c.X - pwmFt.Width / 2, c.Y - 22));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var pts = _points.Select(ToScreen).ToArray();
        for (int i = 0; i < pts.Length; i++)
        {
            if ((pos - pts[i]).Length <= NodeRadius + 8)
            {
                _dragIndex = i;
                CaptureMouse();
                InvalidateVisual();
                break;
            }
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            int pwm = PwmFromY(pos.Y);
            var old = _points[_dragIndex];
            _points[_dragIndex] = old with { Pwm = pwm };
            InvalidateVisual();
            CurveChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
        base.OnMouseLeftButtonUp(e);
    }
}
