namespace GamingCenter.UI.Hardware;

/// <summary>
/// Structured, dashboard-ready telemetry. Every field is nullable: null means
/// "this machine's sensors don't expose it" (e.g. the i7-8750H exposes no CPU
/// temperature via LibreHardwareMonitor), and the UI must say so honestly
/// rather than showing a wrong or zero value.
/// </summary>
public sealed record Telemetry
{
    // CPU
    public double? CpuLoadTotal { get; init; }     // %
    public double? CpuLoadMax { get; init; }       // % hottest thread
    public double? CpuTempC { get; init; }          // °C (often null on this platform)
    public double? CpuClockMhz { get; init; }       // MHz max core
    public double? CpuPowerW { get; init; }         // W package
    public double? CpuVoltage { get; init; }        // V

    // GPU (discrete preferred)
    public string? GpuName { get; init; }
    public double? GpuLoad { get; init; }           // % core
    public double? GpuTempC { get; init; }          // °C core
    public double? GpuHotSpotC { get; init; }       // °C hot spot
    public double? GpuClockMhz { get; init; }       // MHz core
    public double? GpuMemClockMhz { get; init; }    // MHz memory
    public double? GpuPowerW { get; init; }         // W package
    public double? GpuVramUsedMb { get; init; }
    public double? GpuVramTotalMb { get; init; }

    // Memory
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }        // used + available
    public double? RamLoad { get; init; }           // %
    public double? SwapUsedGb { get; init; }        // commit / virtual
    public double? SwapTotalGb { get; init; }
    public double? SwapLoad { get; init; }          // %

    // Fan (usually EC-only on this platform; null when unavailable via LHM)
    public double? FanRpm { get; init; }

    public bool CpuTempAvailable => CpuTempC is not null;
    public bool FanRpmAvailable => FanRpm is not null;
}
