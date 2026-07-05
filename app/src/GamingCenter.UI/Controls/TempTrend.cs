using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Temperature trajectory chart (research spec §4): CPU + GPU temp only, two
/// lines on a shared FIXED 30–100 °C axis (no auto-scale, no zero baseline).
/// Faint severity zones (80–90 amber, 90–100 red) and dashed reference lines
/// (GPU 87 clock-down, CPU 95 Tjmax-warn). 60 s rolling window at 1 Hz.
/// Series color = identity (CPU magenta, GPU cyan), never severity. Gaps shown.
/// </summary>
public sealed class TempTrend : FrameworkElement
{
    private const double YMin = 30, YMax = 100;
    private const int Capacity = 60; // 60 s at 1 Hz

    private readonly List<double?> _cpu = new();
    private readonly List<double?> _gpu = new();

    private static readonly Color CpuColor = Brand.Magenta; // identity
    private static readonly Color GpuColor = Brand.Cyan;
    private static readonly Brush Ink3 = Brand.Frozen(Brand.Ink3);
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Code, Consolas, monospace"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Append one sample per series (null = sensor unavailable that tick; drawn as a gap).</summary>
    public void Push(double? cpuTemp, double? gpuTemp)
    {
        _cpu.Add(cpuTemp);
        _gpu.Add(gpuTemp);
        if (_cpu.Count > Capacity) _cpu.RemoveRange(0, _cpu.Count - Capacity);
        if (_gpu.Count > Capacity) _gpu.RemoveRange(0, _gpu.Count - Capacity);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double padL = 34, padR = 8, padT = 6, padB = 16;
        var plot = new Rect(padL, padT, Math.Max(1, w - padL - padR), Math.Max(1, h - padT - padB));

        double YtoPix(double c) => plot.Bottom - (Math.Clamp(c, YMin, YMax) - YMin) / (YMax - YMin) * plot.Height;

        // Severity zones (faint): 80–90 amber, 90–100 red.
        DrawZone(dc, plot, YtoPix(80), YtoPix(90), Color.FromArgb(0x14, 0xFB, 0xBF, 0x24));
        DrawZone(dc, plot, YtoPix(90), YtoPix(100), Color.FromArgb(0x16, 0xFF, 0x33, 0x55));

        // Gridlines + Y labels at 30/50/70/90.
        foreach (var g in new[] { 30, 50, 70, 90 })
        {
            double y = YtoPix(g);
            dc.DrawLine(new Pen(Brand.Frozen(Color.FromArgb(0x30, 0x3A, 0x21, 0x60)), 1),
                new Point(plot.Left, y), new Point(plot.Right, y));
            var ft = new FormattedText($"{g}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                MonoFace, 9.5, Ink3, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(plot.Left - ft.Width - 6, y - ft.Height / 2));
        }

        // Reference lines: GPU 87 (cyan dashed), CPU 95 (magenta dashed).
        DashLine(dc, plot, YtoPix(87), GpuColor);
        DashLine(dc, plot, YtoPix(95), CpuColor);

        // Series.
        DrawSeries(dc, plot, _gpu, GpuColor, YtoPix);
        DrawSeries(dc, plot, _cpu, CpuColor, YtoPix);
    }

    private static void DrawZone(DrawingContext dc, Rect plot, double yTop, double yBottom, Color c)
    {
        var b = new SolidColorBrush(c); b.Freeze();
        dc.DrawRectangle(b, null, new Rect(plot.Left, yTop, plot.Width, Math.Max(0, yBottom - yTop)));
    }

    private void DashLine(DrawingContext dc, Rect plot, double y, Color c)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x88, c.R, c.G, c.B)), 1)
        { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
        pen.Freeze();
        dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
    }

    private void DrawSeries(DrawingContext dc, Rect plot, List<double?> data, Color color, Func<double, double> YtoPix)
    {
        if (data.Count < 2) return;
        double stepX = plot.Width / (Capacity - 1);
        double x0 = plot.Right - (data.Count - 1) * stepX; // newest at right edge

        var pen = new Pen(new SolidColorBrush(color), 2)
        { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        // Draw contiguous non-null runs as separate figures (gaps shown, not bridged).
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool open = false;
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i] is double v)
                {
                    var p = new Point(x0 + i * stepX, YtoPix(v));
                    if (!open) { ctx.BeginFigure(p, false, false); open = true; }
                    else ctx.LineTo(p, true, true);
                }
                else open = false;
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
