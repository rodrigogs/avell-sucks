using System;
using System.Windows.Media;

namespace AvellSucks.UI.Controls;

/// <summary>
/// Thermal band model: maps a temperature to a severity band + color, with
/// laptop-tuned thresholds that differ for CPU (Tjmax ~100°C) and NVIDIA GPU
/// (software clock-down ~87°C). Source: dashboard research spec §1.
/// </summary>
public enum ThermalBand { Cold, Normal, Warm, Hot, Critical }

public enum ThermalKind { Cpu, Gpu }

public static class Thermal
{
    // Severity ramp — the only place green/amber/red appear. Cold reuses the
    // brand cyan; the warmer bands are thermal-specific and intentionally distinct
    // from the brand accents.
    public static readonly Color Cold = Brand.Cyan;                          // cyan
    public static readonly Color Normal = Color.FromRgb(0x35, 0xF0, 0xA0);   // green
    public static readonly Color Warm = Color.FromRgb(0xFB, 0xBF, 0x24);     // amber
    public static readonly Color Hot = Color.FromRgb(0xFF, 0x8A, 0x3D);      // orange
    public static readonly Color Critical = Color.FromRgb(0xFF, 0x33, 0x55); // red

    /// <summary>Band for a temperature. CPU and GPU use different laptop-tuned cutoffs.</summary>
    public static ThermalBand BandFor(double tempC, ThermalKind kind) => kind switch
    {
        // CPU: cold ≤45, normal 46–74, warm 75–84, hot 85–94, critical ≥95
        ThermalKind.Cpu =>
            tempC <= 45 ? ThermalBand.Cold
            : tempC < 75 ? ThermalBand.Normal
            : tempC < 85 ? ThermalBand.Warm
            : tempC < 95 ? ThermalBand.Hot
            : ThermalBand.Critical,
        // GPU (NVIDIA laptop): cold ≤45, normal 46–79, warm 80–86, hot 87–101, critical ≥102
        _ =>
            tempC <= 45 ? ThermalBand.Cold
            : tempC < 80 ? ThermalBand.Normal
            : tempC < 87 ? ThermalBand.Warm
            : tempC < 102 ? ThermalBand.Hot
            : ThermalBand.Critical,
    };

    public static Color ColorFor(ThermalBand band) => band switch
    {
        ThermalBand.Cold => Cold,
        ThermalBand.Normal => Normal,
        ThermalBand.Warm => Warm,
        ThermalBand.Hot => Hot,
        _ => Critical,
    };

    /// <summary>
    /// Normalized heat 0..1 across the readable range (30→100°C), used to
    /// interpolate the glow bloom (BlurRadius 8→28, Opacity 0.5→0.9).
    /// </summary>
    public static double Heat01(double tempC) => Math.Clamp((tempC - 30.0) / (100.0 - 30.0), 0, 1);

    public static double GlowBlur(double tempC) => 8 + Heat01(tempC) * 20;   // 8 → 28 px
    public static double GlowOpacity(double tempC) => 0.5 + Heat01(tempC) * 0.4; // 0.5 → 0.9
}
