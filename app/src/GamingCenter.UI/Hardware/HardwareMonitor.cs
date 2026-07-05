using LibreHardwareMonitor.Hardware;

namespace GamingCenter.UI.Hardware;

/// <summary>
/// Wraps LibreHardwareMonitor to collect CPU/GPU temps, clocks, loads, and fan RPMs.
/// Must run as admin for ring-0 sensor access.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly CpuThermalZone _cpuThermal = new();

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };
        _computer.Open();
    }

    public void Update()
    {
        _computer.Accept(_visitor);
    }

    public HardwareSnapshot GetSnapshot()
    {
        var sensors = new List<SensorReading>();

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            CollectSensors(hw, sensors);
            foreach (var sub in hw.SubHardware)
            {
                sub.Update();
                CollectSensors(sub, sensors);
            }
        }

        return new HardwareSnapshot(DateTimeOffset.UtcNow, sensors);
    }

    private static void CollectSensors(IHardware hw, List<SensorReading> sensors)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.Value is null) continue;

            sensors.Add(new SensorReading(
                Hardware: hw.Name,
                SensorType: s.SensorType.ToString(),
                Name: s.Name,
                Value: Math.Round(s.Value.Value, 1),
                Max: s.Max is null ? null : Math.Round(s.Max.Value, 1)));
        }
    }

    /// <summary>
    /// Builds structured, dashboard-ready telemetry with precise sensor
    /// selection (matched against the real machine's sensor names, not guessed).
    /// Fields stay null when the platform doesn't expose them.
    /// </summary>
    public Telemetry GetTelemetry()
    {
        var cpu = new List<ISensor>();
        var mem = new List<ISensor>();
        var virt = new List<ISensor>();
        ISensor[]? discreteGpu = null;
        int discreteScore = -1;

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();

            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu.AddRange(hw.Sensors);
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    // Prefer the discrete GPU: rank by whether it reports a core
                    // temperature and dedicated VRAM (integrated GPUs usually don't).
                    int score =
                        (hw.Sensors.Any(s => s.SensorType == SensorType.Temperature) ? 2 : 0) +
                        (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd ? 1 : 0);
                    if (score > discreteScore)
                    {
                        discreteScore = score;
                        discreteGpu = hw.Sensors.ToArray();
                        _discreteGpuName = hw.Name;
                    }
                    break;

                case HardwareType.Memory:
                    // The library exposes two memory hardware nodes: physical
                    // ("Total Memory") and commit ("Virtual Memory").
                    if (hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                        virt.AddRange(hw.Sensors);
                    else
                        mem.AddRange(hw.Sensors);
                    break;
            }
        }

        double? Val(IEnumerable<ISensor> src, SensorType type, params string[] names) =>
            src.FirstOrDefault(s => s.SensorType == type && s.Value is not null &&
                                    names.Any(n => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)))
               ?.Value is float v ? Math.Round(v, 1) : null;

        double? First(IEnumerable<ISensor> src, SensorType type, Func<string, bool> match) =>
            src.FirstOrDefault(s => s.SensorType == type && s.Value is not null && match(s.Name))
               ?.Value is float v ? Math.Round(v, 1) : null;

        var gpu = discreteGpu ?? Array.Empty<ISensor>();

        return new Telemetry
        {
            // CPU — this platform exposes load only; temp/clock/power may be null.
            CpuLoadTotal = Val(cpu, SensorType.Load, "CPU Total"),
            CpuLoadMax = Val(cpu, SensorType.Load, "CPU Core Max"),
            // Prefer the library sensor; fall back to the OEM's Thermal Zone
            // counter (the source that matches the original Gaming Center).
            CpuTempC = Val(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max", "CPU Cores")
                       ?? _cpuThermal.ReadCelsius(),
            CpuClockMhz = First(cpu, SensorType.Clock, n => n.Contains("Core", StringComparison.OrdinalIgnoreCase))
                          ?? _cpuThermal.ReadClockMhz(),
            CpuPowerW = Val(cpu, SensorType.Power, "CPU Package", "Package"),
            CpuVoltage = First(cpu, SensorType.Voltage, n => n.Contains("Core", StringComparison.OrdinalIgnoreCase)),

            // GPU — discrete card reports the full set.
            GpuName = _discreteGpuName,
            GpuLoad = Val(gpu, SensorType.Load, "GPU Core"),
            GpuTempC = Val(gpu, SensorType.Temperature, "GPU Core"),
            GpuHotSpotC = Val(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuClockMhz = Val(gpu, SensorType.Clock, "GPU Core"),
            GpuMemClockMhz = Val(gpu, SensorType.Clock, "GPU Memory"),
            GpuPowerW = Val(gpu, SensorType.Power, "GPU Package"),
            GpuVramUsedMb = Val(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            GpuVramTotalMb = Val(gpu, SensorType.SmallData, "GPU Memory Total"),

            // Memory — physical vs commit/swap, kept distinct.
            RamUsedGb = Val(mem, SensorType.Data, "Memory Used"),
            RamTotalGb = Sum(Val(mem, SensorType.Data, "Memory Used"), Val(mem, SensorType.Data, "Memory Available")),
            RamLoad = Val(mem, SensorType.Load, "Memory"),
            SwapUsedGb = Val(virt, SensorType.Data, "Memory Used"),
            SwapTotalGb = Sum(Val(virt, SensorType.Data, "Memory Used"), Val(virt, SensorType.Data, "Memory Available")),
            SwapLoad = Val(virt, SensorType.Load, "Memory"),

            // Fan — LHM reports none on this Avell; EC is the real source (wired separately).
            FanRpm = First(cpu.Concat(gpu), SensorType.Fan, _ => true),
        };
    }

    private string? _discreteGpuName;

    private static double? Sum(double? a, double? b) =>
        a is null && b is null ? null : Math.Round((a ?? 0) + (b ?? 0), 1);

    public void Dispose()
    {
        _cpuThermal.Dispose();
        _computer.Close();
    }
}

public sealed record HardwareSnapshot(DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Sensors);

public sealed record SensorReading(
    string Hardware,
    string SensorType,   // Temperature, Clock, Load, Fan, Voltage, etc.
    string Name,
    double Value,
    double? Max);

public sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware) { }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
