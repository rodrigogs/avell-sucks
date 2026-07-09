using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Controls;

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
    private int _hoverIndex = -1;

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
    private static readonly Brush GridBrushSoft = Brand.Frozen(Color.FromArgb(0x24, 0x3A, 0x2F, 0x47));
    private static readonly Brush InkBrush = Brand.Frozen(Brand.Ink);
    private static readonly Brush Ink3Brush = Brand.Frozen(Brand.Ink3);
    // Dimmer than the node labels: the axis bounds (40°/100°) are context, not
    // editable breakpoints, so they must read as quieter than the five nodes.
    private static readonly Brush BoundBrush = Brand.Frozen(Color.FromArgb(0x88, Brand.Ink3.R, Brand.Ink3.G, Brand.Ink3.B));
    private static readonly Brush NodeFill = Brand.Frozen(Brand.Bg);

    // The curve reads cool→hot along its own X axis (temperature): cyan at 40°,
    // through violet, to magenta at 100° — the same neon axis as the rest of the
    // app, and "danger legible" as the line climbs into the hot zone. The stroke
    // and the fill under it share the gradient so the envelope reads as one body.
    private static readonly GradientStopCollection CurveStops = new()
    {
        new GradientStop(Brand.Cyan, 0.0),
        new GradientStop(Brand.Violet, 0.5),
        new GradientStop(Brand.Magenta, 1.0),
    };
    private static readonly Pen LinePen = FrozenPen(
        new LinearGradientBrush(CurveStops, new Point(0, 0), new Point(1, 0)), 2.5, PenLineJoin.Round);
    private static readonly Brush CurveFill = FrozenBrush(new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(0x33, Brand.Cyan.R, Brand.Cyan.G, Brand.Cyan.B), 0.0),
        new GradientStop(Color.FromArgb(0x33, Brand.Magenta.R, Brand.Magenta.G, Brand.Magenta.B), 1.0),
    }, new Point(0, 0), new Point(1, 0)));
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Pen FrozenPen(Color c, double thickness, PenLineJoin join = PenLineJoin.Miter)
        => FrozenPen(Brand.Frozen(c), thickness, join);

    private static Pen FrozenPen(Brush b, double thickness, PenLineJoin join = PenLineJoin.Miter)
    {
        if (b.CanFreeze && !b.IsFrozen) b.Freeze();
        var pen = new Pen(b, thickness) { LineJoin = join };
        pen.Freeze();
        return pen;
    }

    private static Brush FrozenBrush(Brush b)
    {
        if (b.CanFreeze && !b.IsFrozen) b.Freeze();
        return b;
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

    // Color on the cool→hot curve axis (cyan @40° → violet @70° → magenta @100°),
    // matching the stroke gradient so a node's ring belongs to the line it sits on.
    private static Color CurveColorAt(double tempC)
    {
        double f = Math.Clamp((tempC - TMin) / (TMax - TMin), 0, 1);
        return f <= 0.5
            ? Lerp(Brand.Cyan, Brand.Violet, f / 0.5)
            : Lerp(Brand.Violet, Brand.Magenta, (f - 0.5) / 0.5);
    }

    private static Color Lerp(Color a, Color b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * f),
            (byte)(a.G + (b.G - a.G) * f),
            (byte)(a.B + (b.B - a.B) * f));
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

        // Vertical guides at each node's temperature breakpoint — soft, so you can
        // read a node's PWM straight down to its °C label without a ruler.
        var pts = _points.Select(ToScreen).ToArray();
        var vGridPen = new Pen(GridBrushSoft, 1);
        foreach (var p in pts)
            dc.DrawLine(vGridPen, new Point(p.X, r.Top), new Point(p.X, r.Bottom));

        // Axis bounds. The nodes only span 50–90 °C but the plot (and the live
        // residency / operating point) runs the full 40–100 °C the sensors report,
        // so mark the two ends — a quiet edge guide + a dimmed label — to make the
        // extra range explicit rather than dead margin (a 46° idle GPU or a 98°
        // spike lands here, outside the editable breakpoints).
        foreach (var bound in new[] { TMin, TMax })
        {
            double bx = TempToX(bound);
            dc.DrawLine(vGridPen, new Point(bx, r.Top), new Point(bx, r.Bottom));
            var bft = Text($"{bound:0}°", 9.5, BoundBrush);
            dc.DrawText(bft, new Point(bx - bft.Width / 2, r.Bottom + 6));
        }

        // Horizontal gridlines + PWM% labels at 0/25/50/75/100.
        for (int i = 0; i <= 4; i++)
        {
            double y = r.Bottom - i / 4.0 * r.Height;
            dc.DrawLine(new Pen(GridBrush, 1), new Point(r.Left, y), new Point(r.Right, y));
            var pctFt = Text($"{i * 25}", 10, Ink3Brush);
            dc.DrawText(pctFt, new Point(r.Left - 26, y - pctFt.Height / 2));
        }

        // Filled envelope under the curve (cool→hot gradient, low alpha): the fan's
        // output reads as a body of air moved, not just a wire. Clipped to the plot
        // so the fade never bleeds past the axes.
        var fill = new StreamGeometry();
        using (var fctx = fill.Open())
        {
            fctx.BeginFigure(new Point(pts[0].X, r.Bottom), true, true);
            fctx.LineTo(pts[0], true, true);
            for (int i = 1; i < pts.Length; i++) fctx.LineTo(pts[i], true, true);
            fctx.LineTo(new Point(pts[^1].X, r.Bottom), true, true);
        }
        fill.Freeze();
        dc.DrawGeometry(CurveFill, null, fill);

        // Curve line (over the thermal layer + its own fill).
        var line = new StreamGeometry();
        using (var lctx = line.Open())
        {
            lctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Length; i++) lctx.LineTo(pts[i], true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, LinePen, line);

        // Nodes + labels. Each node takes its color from its spot on the cool→hot
        // gradient, so it reads as part of the curve, not a separate magenta layer.
        // States: default ring, hover halo, active (dragging) larger ring + glow.
        for (int i = 0; i < pts.Length; i++)
        {
            var c = pts[i];
            bool active = i == _dragIndex;
            bool hover = i == _hoverIndex && !active;
            var nodeColor = CurveColorAt(_points[i].TemperatureC);

            if (active)
                dc.DrawEllipse(Brand.Frozen(Color.FromArgb(0x33, nodeColor.R, nodeColor.G, nodeColor.B)),
                    null, c, NodeRadius + 8, NodeRadius + 8);
            else if (hover)
                dc.DrawEllipse(Brand.Frozen(Color.FromArgb(0x22, nodeColor.R, nodeColor.G, nodeColor.B)),
                    null, c, NodeRadius + 5, NodeRadius + 5);

            double rad = NodeRadius + (active ? 2 : hover ? 1 : 0);
            dc.DrawEllipse(NodeFill, FrozenPen(nodeColor, active ? 3 : 2.5), c, rad, rad);
            dc.DrawEllipse(Brand.Frozen(nodeColor), null, c, 2.5, 2.5);

            var tempFt = Text($"{_points[i].TemperatureC}°", 10.5, Ink3Brush);
            dc.DrawText(tempFt, new Point(c.X - tempFt.Width / 2, r.Bottom + 6));

            // The PWM% label lifts to full-ink and grows a touch on the active node,
            // so the value you're setting is the one that stands out.
            int pwmPct = (int)Math.Round(_points[i].Pwm / (double)MaxPwm * 100);
            var pwmFt = Text($"{pwmPct}%", active ? 12.5 : 11, active ? Brand.Frozen(nodeColor) : InkBrush);
            dc.DrawText(pwmFt, new Point(c.X - pwmFt.Width / 2, c.Y - 22));
        }

        // Live operating points: a thin guide from the axis to the curve + a
        // glowing dot where the machine sits right now. The numeric readout lives
        // in a fixed corner box (below) so two close sensors never overlap pills.
        DrawOperatingGuide(dc, r, LastSample(_gpuHist), GpuColor);
        DrawOperatingGuide(dc, r, LastSample(_cpuHist), CpuColor);
        DrawLiveReadout(dc, r);
    }

    private void DrawTempBand(DrawingContext dc, Rect r, double t0, double t1, Color c)
    {
        double x0 = TempToX(t0), x1 = TempToX(t1);
        dc.DrawRectangle(Brand.Frozen(c), null, new Rect(x0, r.Top, Math.Max(0, x1 - x0), r.Height));
    }

    // Residency = how much of the 60 s window each sensor spent at each
    // temperature, drawn as a low "carpet" hugging the X axis (never spikes that
    // fight the curve). Height is gently compressed (sqrt) and capped low; each
    // column fades upward via a vertical gradient so it reads as ambient density.
    // A faint baseline tint marks every temperature the sensor visited, so a
    // single stable temperature still reads as presence, not a lone bar.
    private void DrawResidency(DrawingContext dc, Rect r)
    {
        DrawResidencySeries(dc, r, Bin(_gpuHist), GpuColor);
        DrawResidencySeries(dc, r, Bin(_cpuHist), CpuColor);
    }

    private void DrawResidencySeries(DrawingContext dc, Rect r, int[] bins, Color color)
    {
        int max = 0;
        foreach (var b in bins) max = Math.Max(max, b);
        if (max == 0) return;

        double binW = r.Width / HistBins;
        double capH = r.Height * 0.30; // low ceiling — a carpet, not a chart

        for (int i = 0; i < HistBins; i++)
        {
            if (bins[i] == 0) continue;
            double x = r.Left + i * binW;
            // sqrt compresses the peak so a long-dwell bin doesn't tower; +baseline
            // floor so a brief visit still shows.
            double frac = Math.Sqrt(bins[i] / (double)max);
            double h = capH * (0.18 + 0.82 * frac);

            var col = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x40, color.R, color.G, color.B), 0.0),
                    new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0),
                }
            };
            col.Freeze();
            dc.DrawRectangle(col, null, new Rect(x, r.Bottom - h, binW + 0.5, h));
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

    // A thin dashed guide from the axis to the curve + a glowing dot at the live
    // operating point. No text here — the numbers live in the fixed readout, so
    // two sensors at nearby temperatures never overlap.
    private void DrawOperatingGuide(DrawingContext dc, Rect r, double? tempC, Color color)
    {
        if (tempC is not double t) return;

        double x = TempToX(t);
        double pwm = PwmAtTemp(t);
        double y = r.Bottom - Math.Clamp(pwm / MaxPwm, 0, 1) * r.Height;
        var dot = new Point(x, y);

        var guide = new Pen(Brand.Frozen(Color.FromArgb(0x77, color.R, color.G, color.B)), 1.5)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        guide.Freeze();
        dc.DrawLine(guide, new Point(x, r.Bottom), dot);

        dc.DrawEllipse(Brand.Frozen(Color.FromArgb(0x33, color.R, color.G, color.B)), null, dot, 8, 8);
        dc.DrawEllipse(Brand.Frozen(color), FrozenPen(Brand.Bg, 1.5), dot, 4.5, 4.5);
    }

    // Consolidated live readout, top-left inside the plot: one row per active
    // sensor — "CPU  57° → 41%" (current temp → fan % the curve commands). Fixed
    // position, so it never collides with the curve, nodes, or the other sensor.
    private void DrawLiveReadout(DrawingContext dc, Rect r)
    {
        var rows = new List<(string label, double t, Color c)>();
        if (LastSample(_cpuHist) is double ct) rows.Add(("CPU", ct, CpuColor));
        if (LastSample(_gpuHist) is double gt) rows.Add(("GPU", gt, GpuColor));
        if (rows.Count == 0) return;

        const double padX = 10, padY = 7, lineH = 17, boxW = 132;
        double boxH = padY * 2 + lineH * rows.Count;
        var box = new Rect(r.Left + 10, r.Top + 10, boxW, boxH);
        dc.DrawRoundedRectangle(Brand.Frozen(Color.FromArgb(0xCC, 0x1C, 0x16, 0x22)),
            FrozenPen(Color.FromArgb(0x40, 0x3A, 0x2F, 0x47), 1), box, 6, 6);

        for (int i = 0; i < rows.Count; i++)
        {
            var (label, t, c) = rows[i];
            int pct = (int)Math.Round(PwmAtTemp(t) / MaxPwm * 100);
            double y = box.Top + padY + i * lineH;

            dc.DrawEllipse(Brand.Frozen(c), null, new Point(box.Left + padX + 3, y + 7), 3, 3);
            dc.DrawText(Text(label, 11, Ink3Brush), new Point(box.Left + padX + 12, y));
            var val = Text($"{t:0}° → {pct}%", 11.5, Brand.Frozen(c));
            dc.DrawText(val, new Point(box.Right - padX - val.Width, y - 0.5));
        }
    }

    private FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int hit = HitTest(e.GetPosition(this));
        if (hit >= 0)
        {
            _dragIndex = hit;
            CaptureMouse();
            InvalidateVisual();
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            int pwm = PwmFromY(pos.Y);
            var old = _points[_dragIndex];
            _points[_dragIndex] = old with { Pwm = pwm };
            InvalidateVisual();
            CurveChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Hover feedback: which node the pointer is over drives the halo + the
            // grab cursor, so a draggable node signals itself before you click.
            int hit = HitTest(pos);
            if (hit != _hoverIndex)
            {
                _hoverIndex = hit;
                Cursor = hit >= 0 ? Cursors.SizeNS : Cursors.Arrow;
                InvalidateVisual();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
        base.OnMouseLeave(e);
    }

    private int HitTest(Point pos)
    {
        var pts = _points.Select(ToScreen).ToArray();
        for (int i = 0; i < pts.Length; i++)
            if ((pos - pts[i]).Length <= NodeRadius + 8) return i;
        return -1;
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
