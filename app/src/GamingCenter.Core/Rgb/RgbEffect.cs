namespace GamingCenter.Core.Rgb;

public enum RgbEffectType
{
    Static = 0,
    Breathing = 1,
    ColorCycle = 2,
    Wave = 3,
    Ripple = 4,
    Custom = 5
}

public enum RgbSpeed
{
    Slow = 0,
    Normal = 1,
    Fast = 2
}

public enum RgbDirection
{
    LeftToRight = 0,
    RightToLeft = 1,
    TopToBottom = 2,
    BottomToTop = 3,
    CenterOut = 4
}

public enum RgbZoneKind
{
    FullKeyboard,
    Wsad,
    NumberRow,
    ArrowKeys
}

public sealed record RgbEffectResult(bool Success, string? Error = null);

public sealed record RgbEffect(
    RgbEffectType Type,
    RgbZoneKind Zone,
    IReadOnlyList<RgbColor> Colors,
    RgbSpeed Speed = RgbSpeed.Normal,
    RgbDirection Direction = RgbDirection.LeftToRight,
    byte Brightness = 100,
    bool Save = false
)
{
    public static RgbEffect SingleColor(RgbColor color, byte brightness = 100) =>
        new(RgbEffectType.Static, RgbZoneKind.FullKeyboard, [color], Brightness: brightness);

    public string FriendlyName => Type switch
    {
        RgbEffectType.Static => "Static",
        RgbEffectType.Breathing => "Breathing",
        RgbEffectType.ColorCycle => "Color cycle",
        RgbEffectType.Wave => "Wave",
        RgbEffectType.Ripple => "Ripple",
        _ => "Custom"
    };
}
