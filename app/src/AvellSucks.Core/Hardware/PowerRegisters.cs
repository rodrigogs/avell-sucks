namespace AvellSucks.Core.Hardware;

/// <summary>
/// Single source of truth for the CPU power-limit EC registers, reverse-engineered
/// from the decompiled OEM app and confirmed on hardware. Every layer — the WMI
/// power service, the EC backend read path, the ASP.NET PowerController, and the
/// write allowlist — references these addresses so the PL surface can never
/// silently drift between them (the same guarantee <see cref="FanModeMap"/> gives
/// the fan surface).
///
///   0x783/0x784/0x785 (1923/1924/1925) — PL1/PL2/PL4 setting bytes (watts)
///   0x730/0x731/0x732 (1840/1841/1842) — Gaming-mode PL defaults (read-only)
///   0x734/0x735/0x736 (1844/1845/1846) — Office-mode PL defaults (read-only)
///
/// (FanManagementPage2.SetPL1/2/4Value → WMIWriteECRAM(1923/1924/1925);
/// GetGamingPLDefaultValue reads 1840.., GetOfficePLDefaultValue reads 1844..).
/// Tau is NOT an EC register — the OEM sets it via Intel XTU, so it is out of scope.
/// </summary>
public static class PowerRegisters
{
    /// <summary>PL1 sustained setting byte (0x783).</summary>
    public const int Pl1 = 1923;
    /// <summary>PL2 turbo setting byte (0x784).</summary>
    public const int Pl2 = 1924;
    /// <summary>PL4 peak setting byte (0x785).</summary>
    public const int Pl4 = 1925;

    /// <summary>Gaming-mode PL default registers (read-only): PL1/PL2/PL4.</summary>
    public static readonly int[] GamingDefaults = [1840, 1841, 1842];

    /// <summary>Office-mode PL default registers (read-only): PL1/PL2/PL4.</summary>
    public static readonly int[] OfficeDefaults = [1844, 1845, 1846];

    /// <summary>The three writable PL setting addresses, in PL1/PL2/PL4 order.</summary>
    public static readonly int[] Setting = [Pl1, Pl2, Pl4];

    /// <summary>Max raw byte value a PL register accepts (254 W); the UI clamps tighter.</summary>
    public const int MaxWatts = 254;
}
