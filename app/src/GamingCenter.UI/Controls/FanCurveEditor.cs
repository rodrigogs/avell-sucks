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
/// Draggable temp→PWM fan curve with a live thermal layer. Five nodes on a
/// plotted line; drag a node vertically to set its PWM. Temperatures are fixed
/// breakpoints on the X axis (40–100 °C).
///
/// The background is a residency histogram — how much of the last 60 s the CPU
/// and GPU spent at each temperature — so the user sees *where the machine
/// actually runs* directly under the curve they're editing. Because the curve's
/// X axis already is temperature, the thermal layer aligns exactly with it (no
/// time-vs-temperature confusion). A live operating-point marker (vertical line
/// at the current temperature + a dot where it meets the curve) shows the fan
/// speed the curve would command right now. Series color = identity (CPU
/// magenta, GPU cyan), matching the rest of the app.
/// </summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const int MaxPwm = 140; // 0x8C
    private const double NodeRadius = 7;
    private const double PadLeft = 40, PadRight = 16, PadTop = 20, PadBottom = 26;

    // Temperature axis span (kept in one place; ToScreen and the thermal layer
    // both read it so they can never drift apart).
    private const double TMin = 40, TMax = 100;
    private const int HistBins = 60;     // 1 °C per bin across the span
    private const int HistCapacity = 60; // 60 s at 1 Hz

    private List<FanPoint> _points = new()
    {
        new(50, 40), new(60, 64), new(70, 90), new(80, 116), new(90, 140)
    };
    private int _dragIndex = -1;

    // Rolling temperature history for the residency layer (null = no sample).
    private readonly List<double?> _cpuHist = new();
    private readonly List<double?> _gpuHist = new();

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

    /// <summary>
    /// Append one temperature sample per series to the 60 s residency window
    /// (null = sensor unavailable this tick). Drives the background histogram
    /// and the live operating-point marker.
    /// </summary>
    public void PushTemperature(double? cpuC, double? gpuC)
    {
        _cpuHist.Add(cpuC);
        _gpuHist.Add(gpuC);
        if (_cpuHist.Count > HistCapacity) _cpuHist.RemoveRange(0, _cpuHist.Count - HistCapacity);
        if (_gpuHist.Count > HistCapacity) _gpuHist.RemoveRange(0, _gpuHist.Count - HistCapacity);
        InvalidateVisual();
    }

    private static readonly Color CpuColor = Brand.Magenta; // identity
    private static readonly Color GpuColor = Brand.Cyan;
    private static readonly Brush TrackBrush = Brand.Frozen(Brand.Track);
    private static readonly Brush GridBrush = Brand.Frozen(Color.FromArgb(0x40, 0x3A, 0x2F, 0x47));
    private static readonly Brush InkBrush = Brand.Frozen(Brand.Ink);
    private static readonly Brush Ink3Brush = Brand.Frozen(Brand.Ink3);
    private static readonly Brush NodeFill = Brand.Frozen(Brand.Bg);
    private static readonly Pen LinePen = FrozenPen(Brand.Cyan, 2.5, PenLineJoin.Round);
    private static readonly Pen NodePen = FrozenPen(CpuColor, 2.5);
    private static readonly Brush NodeDot = Brand.Frozen(CpuColor);
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Pen FrozenPen(Color c, double thickness, PenLineJoin join = PenLineJoin.Miter)
    {
        var pen = new Pen(Brand.Frozen(c), thickness) { LineJoin = join };
        pen.Freeze();
        return pen;
    }

    private Rect PlotRect => new(PadLeft, PadTop,
        Math.Max(1, ActualWidth - PadLeft - PadRight),
        Math.Max(1, ActualHeight - PadTop - PadBottom));

    private double TempToX(double tempC)
    {
        var r = PlotRect;
        double tx = (Math.Clamp(tempC, TMin, TMax) - TMin) / (TMax - TMin);
        return r.Left + tx * r.Width;
    }

    private Point ToScreen(FanPoint p)
    {
        var r = PlotRect;
        double tx = (p.TemperatureC - TMin) / (TMax - TMin);
        double ty = p.Pwm / (double)MaxPwm;
        return new Point(r.Left + tx * r.Width, r.Bottom - ty * r.Height);
    }

    private int PwmFromY(double y)
    {
        var r = PlotRect;
        double ty = Math.Clamp((r.Bottom - y) / r.Height, 0, 1);
        return (int)Math.Round(ty * MaxPwm);
    }

    /// <summary>PWM (0..MaxPwm) the current curve commands at a given temperature.</summary>
    private double PwmAtTemp(double tempC)
    {
        if (_points.Count == 0) return 0;
        if (tempC <= _points[0].TemperatureC) return _points[0].Pwm;
        if (tempC >= _points[^1].TemperatureC) return _points[^1].Pwm;
        for (int i = 1; i < _points.Count; i++)
        {
            if (tempC <= _points[i].TemperatureC)
            {
                var a = _points[i - 1];
                var b = _points[i];
                double f = (tempC - a.TemperatureC) / (b.TemperatureC - a.TemperatureC);
                return a.Pwm + f * (b.Pwm - a.Pwm);
            }
        }
        return _points[^1].Pwm;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = PlotRect;
        if (r.Width <= 1 || r.Height <= 1) return;

        // Track.
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);

        // Faint severity bands (X = temperature, so hot temps sit on the right):
        // 80–90 amber, 90–100 red. Ambient context, never competes with data.
        DrawTempBand(dc, r, 80, 90, Color.FromArgb(0x0E, 0xF4, 0xC0, 0x4A));
        DrawTempBand(dc, r, 90, 100, Color.FromArgb(0x12, 0xF5, 0x48, 0x4A));

        // Thermal residency layer (the background "temperature" the user asked for).
        DrawResidency(dc, r);

        // Horizontal gridlines + PWM% labels at 0/25/50/75/100.
        for (int i = 0; i <= 4; i++)
        {
            double y = r.Bottom - i / 4.0 * r.Height;
            dc.DrawLine(new Pen(GridBrush, 1), new Point(r.Left, y), new Point(r.Right, y));
            var pctFt = Text($"{i * 25}", 10, Ink3Brush);
            dc.DrawText(pctFt, new Point(r.Left - 26, y - pctFt.Height / 2));
        }

        // Curve line (over the thermal layer).
        var pts = _points.Select(ToScreen).ToArray();
        var line = new StreamGeometry();
        using (var lctx = line.Open())
        {
            lctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Length; i++) lctx.LineTo(pts[i], true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, LinePen, line);

        // Nodes + labels.
        for (int i = 0; i < pts.Length; i++)
        {
            var c = pts[i];
            bool active = i == _dragIndex;
            double rad = NodeRadius + (active ? 2 : 0);
            dc.DrawEllipse(NodeFill, NodePen, c, rad, rad);
            dc.DrawEllipse(NodeDot, null, c, 2.5, 2.5);

            var tempFt = Text($"{_points[i].TemperatureC}°", 10.5, Ink3Brush);
            dc.DrawText(tempFt, new Point(c.X - tempFt.Width / 2, r.Bottom + 6));

            int pwmPct = (int)Math.Round(_points[i].Pwm / (double)MaxPwm * 100);
            var pwmFt = Text($"{pwmPct}%", 11, InkBrush);
            dc.DrawText(pwmFt, new Point(c.X - pwmFt.Width / 2, c.Y - 22));
        }

        // Live operating-point markers on top: where the machine is right now,
        // and the fan speed the curve commands there.
        DrawOperatingPoint(dc, r, LastSample(_gpuHist), GpuColor, labelAbove: true);
        DrawOperatingPoint(dc, r, LastSample(_cpuHist), CpuColor, labelAbove: false);
    }

    private void DrawTempBand(DrawingContext dc, Rect r, double t0, double t1, Color c)
    {
        double x0 = TempToX(t0), x1 = TempToX(t1);
        var b = new SolidColorBrush(c); b.Freeze();
        dc.DrawRectangle(b, null, new Rect(x0, r.Top, Math.Max(0, x1 - x0), r.Height));
    }

    // Residency = fraction of the 60 s window spent in each 1 °C bin, drawn as
    // translucent bottom-anchored columns. CPU and GPU overlaid; shared max so
    // relative heights are honest. Reads as ambient density, not a second chart.
    private void DrawResidency(DrawingContext dc, Rect r)
    {
        var cpu = Bin(_cpuHist);
        var gpu = Bin(_gpuHist);
        int max = 0;
        for (int i = 0; i < HistBins; i++) max = Math.Max(max, Math.Max(cpu[i], gpu[i]));
        if (max == 0) return;

        double binW = r.Width / HistBins;
        double maxH = r.Height * 0.55; // caps the layer so the curve always dominates
        var cpuBrush = Brand.Frozen(Color.FromArgb(0x3A, CpuColor.R, CpuColor.G, CpuColor.B));
        var gpuBrush = Brand.Frozen(Color.FromArgb(0x3A, GpuColor.R, GpuColor.G, GpuColor.B));

        for (int i = 0; i < HistBins; i++)
        {
            double x = r.Left + i * binW;
            if (gpu[i] > 0)
            {
                double h = gpu[i] / (double)max * maxH;
                dc.DrawRectangle(gpuBrush, null, new Rect(x, r.Bottom - h, binW, h));
            }
            if (cpu[i] > 0)
            {
                double h = cpu[i] / (double)max * maxH;
                dc.DrawRectangle(cpuBrush, null, new Rect(x, r.Bottom - h, binW, h));
            }
        }
    }

    private static int[] Bin(List<double?> hist)
    {
        var bins = new int[HistBins];
        double span = TMax - TMin;
        foreach (var s in hist)
        {
            if (s is not double v) continue;
            int idx = (int)((Math.Clamp(v, TMin, TMax) - TMin) / span * HistBins);
            if (idx >= HistBins) idx = HistBins - 1;
            bins[idx]++;
        }
        return bins;
    }

    private static double? LastSample(List<double?> hist)
    {
        for (int i = hist.Count - 1; i >= 0; i--)
            if (hist[i] is double v) return v;
        return null;
    }

    private void DrawOperatingPoint(DrawingContext dc, Rect r, double? tempC, Color color, bool labelAbove)
    {
        if (tempC is not double t) return;

        double x = TempToX(t);
        double pwm = PwmAtTemp(t);
        double y = r.Bottom - Math.Clamp(pwm / MaxPwm, 0, 1) * r.Height;
        var dot = new Point(x, y);

        // Vertical guide from the axis up to the curve.
        var guide = new Pen(Brand.Frozen(Color.FromArgb(0x99, color.R, color.G, color.B)), 1.5)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        guide.Freeze();
        dc.DrawLine(guide, new Point(x, r.Bottom), new Point(x, dot.Y));

        // Dot on the curve with a soft glow ring.
        dc.DrawEllipse(Brand.Frozen(Color.FromArgb(0x33, color.R, color.G, color.B)), null, dot, 8, 8);
        dc.DrawEllipse(Brand.Frozen(color), FrozenPen(Brand.Bg, 1.5), dot, 4.5, 4.5);

        // Pill: the fan speed the curve commands at this temperature (the output).
        int pct = (int)Math.Round(pwm / MaxPwm * 100);
        var ft = Text($"{pct}%", 10.5, Brand.Frozen(color));
        double pw = ft.Width + 12, ph = ft.Height + 5;
        double py = labelAbove ? dot.Y - ph - 12 : dot.Y + 12;
        py = Math.Clamp(py, r.Top + 2, r.Bottom - ph - 2);
        double px = Math.Clamp(x - pw / 2, r.Left, r.Right - pw);
        var pill = new Rect(px, py, pw, ph);
        dc.DrawRoundedRectangle(Brand.Frozen(Color.FromArgb(0xE6, 0x1C, 0x16, 0x22)),
            FrozenPen(color, 1), pill, 4, 4);
        dc.DrawText(ft, new Point(pill.X + 6, pill.Y + 2.5));
    }

    private FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

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
