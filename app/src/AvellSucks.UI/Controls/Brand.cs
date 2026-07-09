using System.Windows.Media;

namespace AvellSucks.UI.Controls;

/// <summary>
/// Single source of truth for brand colors and a frozen-brush helper, shared by
/// the owner-drawn controls. Keeps the neon palette in one place so it can't
/// drift (previously each control hardcoded its own hex, and two different
/// magentas had crept in). Mirrors Theme/Palette.xaml.
/// </summary>
public static class Brand
{
    // Every value below mirrors the matching Palette.xaml token exactly (hex in
    // the trailing comment). Keep them in lock-step: if a Palette color changes,
    // change it here too, or the owner-drawn controls drift from the styled UI.
    public static readonly Color Magenta = Color.FromRgb(0xFF, 0x2E, 0x88); // MagentaColor
    public static readonly Color Cyan = Color.FromRgb(0x22, 0xD3, 0xEE);    // CyanColor
    public static readonly Color Violet = Color.FromRgb(0xA8, 0x55, 0xF7);  // VioletColor
    public static readonly Color Danger = Color.FromRgb(0xF5, 0x48, 0x4A);  // DangerColor
    public static readonly Color Ok = Color.FromRgb(0x34, 0xE5, 0xA0);      // OkColor
    public static readonly Color Warn = Color.FromRgb(0xF4, 0xC0, 0x4A);    // WarnColor
    public static readonly Color Track = Color.FromRgb(0x24, 0x10, 0x41);   // track/well
    public static readonly Color Ink = Color.FromRgb(0xF2, 0xEE, 0xF6);     // InkColor
    public static readonly Color Ink3 = Color.FromRgb(0x94, 0x8A, 0xA3);    // Ink3Color
    public static readonly Color Bg = Color.FromRgb(0x14, 0x10, 0x18);      // BgColor

    /// <summary>Create a frozen SolidColorBrush (immutable, cross-thread, cheap to reuse).</summary>
    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
