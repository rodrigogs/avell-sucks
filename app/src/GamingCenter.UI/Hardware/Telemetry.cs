namespace GamingCenter.UI.Hardware;

/// <summary>
/// Structured, dashboard-ready telemetry. Every field is nullable: null means
/// "this machine's sensors don't expose it" (e.g. the i7-8750H exposes no CPU
/// power via LibreHardwareMonitor), and the UI says so honestly rather than
/// showing a wrong or zero value. Only fields the UI actually consumes live
/// here — projection of anything unused was dropped.
/// </summary>
public sealed record Telemetry
{
    // CPU
    public string? CpuName { get; init; }
    public double? CpuLoadTotal { get; init; }     // %
    public double? CpuTempC { get; init; }         // °C (via Thermal Zone counter)
    public double? CpuClockMhz { get; init; }      // MHz effective

    // GPU (discrete preferred)
    public string? GpuName { get; init; }
    public double? GpuLoad { get; init; }          // % core
    public double? GpuTempC { get; init; }         // °C core
    public double? GpuHotSpotC { get; init; }      // °C hot spot
    public double? GpuClockMhz { get; init; }      // MHz core
    public double? GpuPowerW { get; init; }        // W package
    public double? GpuVramUsedMb { get; init; }
    public double? GpuVramTotalMb { get; init; }

    // Memory — physical (RAM) vs commit (swap), kept distinct.
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }       // used + available
    public double? RamLoad { get; init; }          // %
    public double? SwapUsedGb { get; init; }       // commit / virtual
    public double? SwapTotalGb { get; init; }
    public double? SwapLoad { get; init; }         // %
}
