using System.Collections.ObjectModel;

namespace GamingCenter.Core.Platforms;

/// <summary>
/// Describes one approved EC write target: a specific address and a set of
/// acceptable values. Writes outside the allowlist are rejected.
/// </summary>
public sealed record EcWriteRule(
    int Address,
    string HexAddress,
    string Description,
    IReadOnlySet<int> AllowedValues)
{
    public EcWriteRule(int address, string description, params int[] allowed)
        : this(address, $"0x{address:X}", description,
               new HashSet<int>(allowed) as IReadOnlySet<int>)
    {
    }
}

/// <summary>
/// Registry of EC addresses and values that may be written when the write
/// gate is open. Derived from the reverse-engineered fan-control map.
/// </summary>
public sealed class EcWriteAllowlist
{
    /// <summary>
    /// Canonical rules for the gaming-center EC fan-control surface.
    /// </summary>
    /// <remarks>
    /// Address 1873 (0x751) — ADDR_MAFAN_CONTROL_BYTE:
    ///   0 = normal/auto, 64 = boost, 160 = custom-advanced,
    ///   129..133 = custom basic levels L1..L5.
    /// Addresses 1859..1863 (0x743..0x747) — PWM source bytes for custom levels.
    /// Addresses 1919..1921 (0x77F..0x781) — speculative PL1/PL2/PL4 power-limit
    /// setting bytes observed as VM-to-EC write candidates in older
    /// GamingCenter tooling. These entries are **wide open (0..255)** on
    /// purpose: the actual semantics and safe value ranges are not yet
    /// confirmed by diffing on real hardware. Tighten explicitly once
    /// validation is complete.
    /// Address 1857 (0x741) — speculative Tau (time window) candidate.
    /// </remarks>
    public static readonly EcWriteRule[] FanControlRules =
    [
        new(1873, "ADDR_MAFAN_CONTROL_BYTE — fan mode",
            0, 64, 160, 129, 130, 131, 132, 133),
        new(1859, "ADDR_MYFAN2_L1_PWM — custom L1 PWM", Enumerable.Range(0, 0x8C + 1).ToArray()),
        new(1860, "ADDR_MYFAN2_L2_PWM — custom L2 PWM", Enumerable.Range(0, 0x8C + 1).ToArray()),
        new(1861, "ADDR_MYFAN2_L3_PWM — custom L3 PWM", Enumerable.Range(0, 0x8C + 1).ToArray()),
        new(1862, "ADDR_MYFAN2_L4_PWM — custom L4 PWM", Enumerable.Range(0, 0x8C + 1).ToArray()),
        new(1863, "ADDR_MYFAN2_L5_PWM — custom L5 PWM", Enumerable.Range(0, 0x8C + 1).ToArray()),

        // Power limits — CONFIRMED from the decompiled OEM GamingCenter
        // (FanManagementPage2.SetPL1/2/4Value → WMIWriteECRAM(1923/1924/1925)).
        // Byte watts (value & 0xFF). These replace the earlier speculative
        // 0x77F..0x781 guesses. Capped at 254 W (raw byte) — the mode presets
        // stay well under; the Advanced sliders clamp to safe ranges in the UI.
        new(1923, "ADDR_PL1_SETTING_VALUE (0x783) — PL1 sustained (W)", Enumerable.Range(0, 255).ToArray()),
        new(1924, "ADDR_PL2_SETTING_VALUE (0x784) — PL2 turbo (W)", Enumerable.Range(0, 255).ToArray()),
        new(1925, "ADDR_PL4_SETTING_VALUE (0x785) — PL4 peak (W)", Enumerable.Range(0, 255).ToArray()),
    ];

    private readonly Dictionary<int, EcWriteRule> _rules;

    public EcWriteAllowlist() : this(FanControlRules)
    {
    }

    public EcWriteAllowlist(IEnumerable<EcWriteRule> rules)
    {
        _rules = rules.ToDictionary(r => r.Address);
        Rules = new ReadOnlyDictionary<int, EcWriteRule>(_rules);
    }

    /// <summary>
    /// All registered rules keyed by address.
    /// </summary>
    public IReadOnlyDictionary<int, EcWriteRule> Rules { get; }

    /// <summary>
    /// True if (address, value) is explicitly allowed.
    /// </summary>
    public bool IsAllowed(int address, int value)
    {
        return _rules.TryGetValue(address, out var rule)
               && rule.AllowedValues.Contains(value);
    }

    /// <summary>
    /// Returns the rule for an address or null if not registered.
    /// </summary>
    public EcWriteRule? GetRule(int address)
        => _rules.TryGetValue(address, out var rule) ? rule : null;
}
