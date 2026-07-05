namespace GamingCenter.Core.Rgb;

public sealed record RgbColor(byte Red, byte Green, byte Blue)
{
    public static RgbColor FromRgb(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);
    public static RgbColor White => new(255, 255, 255);
    public static RgbColor Black => new(0, 0, 0);
    public override string ToString() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}
