namespace GamingCenter.Core.Rgb;

public sealed record RgbState(
    bool PowerOn,
    byte Brightness,
    RgbEffectType Effect,
    RgbDirection Direction,
    RgbSpeed Speed,
    byte LedCount
)
{
    public string FriendlyEffect => Effect switch
    {
        RgbEffectType.Static => "Static",
        RgbEffectType.Breathing => "Breathing",
        RgbEffectType.ColorCycle => "Color cycle",
        RgbEffectType.Wave => "Wave",
        RgbEffectType.Ripple => "Ripple",
        _ => Effect.ToString()
    };
}
