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
    public static readonly Color Magenta = Color.FromRgb(0xFF, 0x2E, 0x88); // == Palette MagentaColor
    public static readonly Color Cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
    public static readonly Color Violet = Color.FromRgb(0xA8, 0x55, 0xF7);
    public static readonly Color Danger = Color.FromRgb(0xF5, 0x48, 0x4A);
    public static readonly Color Ok = Color.FromRgb(0x4A, 0xE8, 0x8A);
    public static readonly Color Track = Color.FromRgb(0x24, 0x10, 0x41);
    public static readonly Color Ink = Color.FromRgb(0xF3, 0xEC, 0xFF);
    public static readonly Color Ink3 = Color.FromRgb(0x7C, 0x6A, 0xA6);
    public static readonly Color Bg = Color.FromRgb(0x14, 0x10, 0x18);

    /// <summary>Create a frozen SolidColorBrush (immutable, cross-thread, cheap to reuse).</summary>
    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
