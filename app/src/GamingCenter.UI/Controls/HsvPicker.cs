using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Compact HSV picker: a saturation/value field plus a hue strip. Exposes the
/// chosen <see cref="SelectedColor"/> and raises <see cref="ColorChanged"/>.
/// Owner-drawn, no external dependency.
/// </summary>
public sealed class HsvPicker : FrameworkElement
{
    private double _hue = 330;   // 0..360
    private double _sat = 0.82;  // 0..1
    private double _val = 1.0;   // 0..1

    private const double StripH = 22;
    private const double Gap = 10;

    private bool _dragField, _dragStrip;

    public event EventHandler? ColorChanged;

    public Color SelectedColor => FromHsv(_hue, _sat, _val);

    public void SetColor(Color c)
    {
        ToHsv(c, out _hue, out _sat, out _val);
        InvalidateVisual();
    }

    private Rect FieldRect => new(0, 0, ActualWidth, Math.Max(1, ActualHeight - StripH - Gap));
    private Rect StripRect => new(0, ActualHeight - StripH, ActualWidth, StripH);

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var field = FieldRect;
        var strip = StripRect;

        // SV field: base hue, white gradient left→right (saturation), black gradient top→bottom (value).
        var hueColor = FromHsv(_hue, 1, 1);
        dc.DrawRoundedRectangle(new SolidColorBrush(hueColor), null, field, 8, 8);

        var satBrush = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0), new Point(1, 0));
        satBrush.Freeze();
        dc.DrawRoundedRectangle(satBrush, null, field, 8, 8);

        var valBrush = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black,
            new Point(0, 0), new Point(0, 1));
        valBrush.Freeze();
        dc.DrawRoundedRectangle(valBrush, null, field, 8, 8);

        // SV cursor.
        double cx = field.Left + _sat * field.Width;
        double cy = field.Top + (1 - _val) * field.Height;
        dc.DrawEllipse(null, new Pen(Brushes.White, 2.5), new Point(cx, cy), 7, 7);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 1), new Point(cx, cy), 8.5, 8.5);

        // Hue strip.
        var hueGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
            hueGrad.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        hueGrad.Freeze();
        dc.DrawRoundedRectangle(hueGrad, null, strip, 6, 6);

        double hx = strip.Left + _hue / 360.0 * strip.Width;
        dc.DrawRoundedRectangle(null, new Pen(Brushes.White, 2.5),
            new Rect(hx - 3, strip.Top - 2, 6, strip.Height + 4), 3, 3);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (StripRect.Contains(p)) { _dragStrip = true; UpdateStrip(p); }
        else if (FieldRect.Contains(p)) { _dragField = true; UpdateField(p); }
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (_dragStrip) UpdateStrip(p);
        else if (_dragField) UpdateField(p);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragField = _dragStrip = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    private void UpdateField(Point p)
    {
        var f = FieldRect;
        _sat = Math.Clamp((p.X - f.Left) / f.Width, 0, 1);
        _val = Math.Clamp(1 - (p.Y - f.Top) / f.Height, 0, 1);
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStrip(Point p)
    {
        var s = StripRect;
        _hue = Math.Clamp((p.X - s.Left) / s.Width, 0, 1) * 360.0;
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public static void ToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }
}
