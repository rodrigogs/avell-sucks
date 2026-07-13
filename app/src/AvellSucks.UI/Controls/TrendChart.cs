using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AvellSucks.UI.Localization;

namespace AvellSucks.UI.Controls;

/// <summary>
/// A 60-second live trend for one processor: temperature drawn as a polyline whose
/// every segment is colored by its own thermal band (cyan→green→amber→orange→red)
/// over a faint bottom-anchored LOAD carpet in the series identity color (CPU
/// magenta, GPU cyan). A glowing dot marks the live (right) edge. Answers the one
/// question the radial gauge can't: "did it spike / throttle while I wasn't
/// watching?". Owner-drawn like <see cref="FanCurveEditor"/>, fed by the existing
/// 1 Hz SensorPump tick via <see cref="PushSample"/> — no timer of its own.
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    // Tight paddings: the readout + time captions were removed as redundant (the
    // gauge above shows the live temp/load; the right edge is "now" by definition),
    // so the plot uses nearly the whole tile.
    private const double PadLeft = 6, PadRight = 6, PadTop = 6, PadBottom = 6;
    private const double TMin = 30, TMax = 105;   // covers CPU crit 95 + GPU crit 102
    private const int HistCapacity = 60;          // 60 s at 1 Hz

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ThermalKind), typeof(TrendChart),
        new FrameworkPropertyMetadata(ThermalKind.Cpu, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>CPU or GPU — drives the identity color of the load carpet.</summary>
    public ThermalKind Kind { get => (ThermalKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    // Rolling windows (null = sensor unavailable that tick — the line breaks, never interpolates).
    private readonly List<double?> _load = new();
    private readonly List<double?> _temp = new();

    /// <summary>Append one sample per series to the 60 s window and repaint.</summary>
    public void PushSample(double? loadPct, double? tempC)
    {
        _load.Add(loadPct);
        _temp.Add(tempC);
        if (_load.Count > HistCapacity) _load.RemoveRange(0, _load.Count - HistCapacity);
        if (_temp.Count > HistCapacity) _temp.RemoveRange(0, _temp.Count - HistCapacity);
        InvalidateVisual();
    }

    private Color IdentityColor => Kind == ThermalKind.Gpu ? Brand.Cyan : Brand.Magenta;

    private static readonly Brush TrackBrush = Brand.Frozen(Brand.Track);
    private static readonly Brush GridBrushSoft = Brand.Frozen(Color.FromArgb(0x24, 0x3A, 0x2F, 0x47));
    private static readonly Brush Ink3Brush = Brand.Frozen(Brand.Ink3);

    // One cached, frozen pen per thermal band — the temp line recolors per segment
    // without allocating a pen each frame (the LoadTempGauge perf rule).
    private static readonly Dictionary<ThermalBand, Pen> BandPens = new()
    {
        [ThermalBand.Cold] = Brand.FrozenPen(Thermal.Cold, 2.5, PenLineJoin.Round),
        [ThermalBand.Normal] = Brand.FrozenPen(Thermal.Normal, 2.5, PenLineJoin.Round),
        [ThermalBand.Warm] = Brand.FrozenPen(Thermal.Warm, 2.5, PenLineJoin.Round),
        [ThermalBand.Hot] = Brand.FrozenPen(Thermal.Hot, 2.5, PenLineJoin.Round),
        [ThermalBand.Critical] = Brand.FrozenPen(Thermal.Critical, 2.5, PenLineJoin.Round),
    };

    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private Rect PlotRect => new(PadLeft, PadTop,
        Math.Max(1, ActualWidth - PadLeft - PadRight),
        Math.Max(1, ActualHeight - PadTop - PadBottom));

    // Newest sample sits at the right edge; index 0 (oldest, up to 60 s ago) at the left.
    private double XForIndex(int i, int count, Rect r)
        => count <= 1 ? r.Right : r.Right - (count - 1 - i) / (double)(HistCapacity - 1) * r.Width;

    private double TempToY(double t, Rect r)
        => r.Bottom - (Math.Clamp(t, TMin, TMax) - TMin) / (TMax - TMin) * r.Height;

    private double LoadToY(double p, Rect r)
        => r.Bottom - Math.Clamp(p, 0, 100) / 100.0 * r.Height;

    protected override void OnRender(DrawingContext dc)
    {
        var r = PlotRect;
        if (r.Width <= 1 || r.Height <= 1) return;

        // Track.
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);

        // Severity bands: faint horizontal reference for the temp axis (and a
        // throttling cue). Each band tint runs from its start temp up to TMax.
        DrawBand(dc, r, WarmStart, TMax, Color.FromArgb(0x0E, Thermal.Warm.R, Thermal.Warm.G, Thermal.Warm.B));
        DrawBand(dc, r, HotStart, TMax, Color.FromArgb(0x10, Thermal.Hot.R, Thermal.Hot.G, Thermal.Hot.B));
        DrawBand(dc, r, CritStart, TMax, Color.FromArgb(0x12, Thermal.Critical.R, Thermal.Critical.G, Thermal.Critical.B));

        // Vertical time gridlines every 15 s.
        var gridPen = new Pen(GridBrushSoft, 1);
        for (int s = 15; s < HistCapacity; s += 15)
        {
            double x = r.Right - s / (double)(HistCapacity - 1) * r.Width;
            dc.DrawLine(gridPen, new Point(x, r.Top), new Point(x, r.Bottom));
        }

        int count = _temp.Count;
        if (count == 0) return;

        // LOAD carpet: bottom-anchored area in the identity color, low alpha.
        DrawLoadCarpet(dc, r, count);

        // TEMP line: per-segment thermal color; breaks at nulls (no fake interpolation).
        bool anyTemp = DrawTempLine(dc, r, count);

        if (!anyTemp)
        {
            // Sensor exposes no temperature — say so, like LoadTempGauge does.
            var na = Text(Loc.T("Gauge.TempNa"), 12, Ink3Brush);
            dc.DrawText(na, new Point(r.Left + (r.Width - na.Width) / 2, r.Top + (r.Height - na.Height) / 2));
        }
    }

    // Thermal band start temps for the current Kind (from Thermal.BandFor cutoffs).
    private double WarmStart => Kind == ThermalKind.Gpu ? 80 : 75;
    private double HotStart => Kind == ThermalKind.Gpu ? 87 : 85;
    private double CritStart => Kind == ThermalKind.Gpu ? 102 : 95;

    private void DrawBand(DrawingContext dc, Rect r, double t0, double t1, Color c)
    {
        double y0 = TempToY(t1, r), y1 = TempToY(t0, r);
        dc.DrawRectangle(Brand.Frozen(c), null, new Rect(r.Left, y0, r.Width, Math.Max(0, y1 - y0)));
    }

    private void DrawLoadCarpet(DrawingContext dc, Rect r, int count)
    {
        var col = IdentityColor;
        var fill = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x33, col.R, col.G, col.B), 1.0), // near baseline
                new GradientStop(Color.FromArgb(0x08, col.R, col.G, col.B), 0.0), // toward top
            },
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool open = false;
            double lastX = r.Left;
            for (int i = 0; i < count; i++)
            {
                if (_load[i] is not double p) continue;
                double x = XForIndex(i, count, r);
                double y = LoadToY(p, r);
                if (!open) { ctx.BeginFigure(new Point(x, r.Bottom), true, true); open = true; }
                ctx.LineTo(new Point(x, y), true, true);
                lastX = x;
            }
            if (open) ctx.LineTo(new Point(lastX, r.Bottom), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, null, geo);
    }

    // Returns true if at least one temperature sample was drawn.
    private bool DrawTempLine(DrawingContext dc, Rect r, int count)
    {
        Point? prev = null;
        bool any = false;
        int lastIdx = -1;
        for (int i = 0; i < count; i++)
        {
            if (_temp[i] is not double t) { prev = null; continue; } // gap — break the line
            any = true; lastIdx = i;
            var p = new Point(XForIndex(i, count, r), TempToY(t, r));
            if (prev is Point a)
            {
                var band = Thermal.BandFor(t, Kind); // segment colored by its endpoint's band
                dc.DrawLine(BandPens[band], a, p);
            }
            prev = p;
        }

        // Live-edge marker on the newest sample, glowing hotter with the band.
        if (lastIdx >= 0 && _temp[lastIdx] is double lt)
        {
            var band = Thermal.BandFor(lt, Kind);
            var c = Thermal.ColorFor(band);
            var dot = new Point(XForIndex(lastIdx, count, r), TempToY(lt, r));
            double halo = band == ThermalBand.Critical ? 11 : 8;
            dc.DrawEllipse(Brand.Frozen(Brand.WithAlpha(c, 0x33)), null, dot, halo, halo);
            dc.DrawEllipse(Brand.Frozen(c), Brand.FrozenPen(Brand.Bg, 1.5), dot, 4.5, 4.5);

            // Peak marker: dashed line + "max NN°" at the 60 s high.
            double peak = double.MinValue;
            foreach (var s in _temp) if (s is double v && v > peak) peak = v;
            if (peak > double.MinValue)
            {
                double py = TempToY(peak, r);
                var peakPen = new Pen(Brand.Frozen(Brand.WithAlpha(Brand.Ink3, 0x66)), 1)
                { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                peakPen.Freeze();
                dc.DrawLine(peakPen, new Point(r.Left, py), new Point(r.Right, py));
                var pk = Text($"max {peak:0}°", 9.5, Ink3Brush);
                dc.DrawText(pk, new Point(r.Right - pk.Width, py - pk.Height - 1));
            }
        }
        return any;
    }

    private FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
