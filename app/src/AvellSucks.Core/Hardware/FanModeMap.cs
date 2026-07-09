namespace AvellSucks.Core.Hardware;

/// <summary>
/// Single source of truth for the fan control surface, reverse-engineered from
/// the OEM app (MyFanCTLByteFlag) and confirmed on hardware. Every layer — the
/// WMI fan service, the ASP.NET FanController, the reconciler, the EC
/// interpreter, and the write allowlist — maps mode keys to the same control
/// bytes here so the table can never silently drift between them.
///
///   0x751 (1873) ADDR_MAFAN_CONTROL_BYTE — the mode byte
///   0x743..0x747 (1859..1863) — custom PWM levels L1..L5
/// </summary>
public static class FanModeMap
{
    /// <summary>ADDR_MAFAN_CONTROL_BYTE (0x751): the fan mode register.</summary>
    public const int ControlByteAddress = 1873;

    /// <summary>Advanced-custom mode byte (0xA0): follows the custom PWM curve.</summary>
    public const int CustomModeByte = 160;

    /// <summary>Max PWM byte for a custom level (0x8C = 140).</summary>
    public const int MaxPwm = 0x8C;

    /// <summary>PWM source bytes for the five custom levels (0x743..0x747).</summary>
    public static readonly int[] CurveAddresses = [1859, 1860, 1861, 1862, 1863];

    /// <summary>Default temperature anchors (°C) paired with the five curve levels.</summary>
    public static readonly int[] DefaultCurveTemps = [50, 60, 70, 80, 90];

    // Canonical mode key → control byte. L1..L5 are 129..133 (0x81..0x85).
    private static readonly Dictionary<string, int> s_keyToByte =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["auto"] = 0, ["boost"] = 64, ["custom"] = 160,
            ["L1"] = 129, ["L2"] = 130, ["L3"] = 131, ["L4"] = 132, ["L5"] = 133,
        };

    private static readonly Dictionary<int, string> s_byteToKey =
        s_keyToByte.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Every control byte in the map (for the write allowlist).</summary>
    public static int[] ControlBytes => s_keyToByte.Values.ToArray();

    /// <summary>Mode key → control byte. Returns false for an unknown key.</summary>
    public static bool TryByteFor(string key, out int value) =>
        s_keyToByte.TryGetValue(key ?? "", out value);

    /// <summary>Control byte → UI mode key; falls back to "auto" for unknown bytes.</summary>
    public static string KeyFor(int controlByte) =>
        s_byteToKey.TryGetValue(controlByte & 0xFF, out var key) ? key : "auto";

    /// <summary>Human-readable description of a raw control byte (for diagnostics).</summary>
    public static string Describe(int controlByte)
    {
        var raw = controlByte & 0xFF;
        return raw switch
        {
            0 => "Normal/Smart",
            64 => "FanBoost/Cold Mode",
            160 => "Advanced Custom",
            >= 129 and <= 133 => $"Custom Level {raw - 128}",
            _ => $"Unknown Control ({raw})",
        };
    }

    /// <summary>Auto-managed states (the EC drives the fan itself): normal + boost.</summary>
    public static bool IsAutoManaged(int controlByte) => (controlByte & 0xFF) is 0 or 64;
}
