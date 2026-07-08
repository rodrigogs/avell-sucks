using LibreHardwareMonitor.Hardware;

namespace AvellSucks.UI.Hardware;

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
        // Only the groups the telemetry actually reads (CPU/GPU/Memory). The
        // dashboard never surfaces motherboard or controller sensors, and this
        // platform exposes no fan tachometer — enabling those groups made every
        // 1 Hz Update() poll hardware whose values are thrown away.
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
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
        string? discreteGpuName = null;  // local, reset every call (no stale carry-over)
        string? cpuName = null;

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();

            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu.AddRange(hw.Sensors);
                    cpuName ??= hw.Name;
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
                        discreteGpuName = hw.Name;
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

        // Match names in the given priority order (not sensor order): the first
        // listed name that has a live sensor wins. Prevents a low-priority match
        // (e.g. per-process D3D VRAM) from shadowing the intended sensor.
        double? Val(IEnumerable<ISensor> src, SensorType type, params string[] names)
        {
            var pool = src.Where(s => s.SensorType == type && s.Value is not null).ToArray();
            foreach (var n in names)
            {
                var hit = pool.FirstOrDefault(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase));
                if (hit?.Value is float v) return Math.Round(v, 1);
            }
            return null;
        }

        double? First(IEnumerable<ISensor> src, SensorType type, Func<string, bool> match) =>
            src.FirstOrDefault(s => s.SensorType == type && s.Value is not null && match(s.Name))
               ?.Value is float v ? Math.Round(v, 1) : null;

        var gpu = discreteGpu ?? Array.Empty<ISensor>();

        return new Telemetry
        {
            // CPU — this platform exposes load only via LHM; temp/clock come from
            // the OEM's Thermal Zone + perf counters (matches the original app).
            CpuName = cpuName,
            CpuLoadTotal = Val(cpu, SensorType.Load, "CPU Total"),
            CpuTempC = Val(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max", "CPU Cores")
                       ?? _cpuThermal.ReadCelsius(),
            CpuClockMhz = First(cpu, SensorType.Clock, n => n.Contains("Core", StringComparison.OrdinalIgnoreCase))
                          ?? _cpuThermal.ReadClockMhz(),

            // GPU — discrete card reports the full set via NVAPI.
            GpuName = discreteGpuName,
            GpuLoad = Val(gpu, SensorType.Load, "GPU Core"),
            GpuTempC = Val(gpu, SensorType.Temperature, "GPU Core"),
            GpuHotSpotC = Val(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuClockMhz = Val(gpu, SensorType.Clock, "GPU Core"),
            GpuPowerW = Val(gpu, SensorType.Power, "GPU Package"),
            // Adapter-wide "GPU Memory Used" is the right headline (total VRAM in
            // use); per-process D3D Dedicated reads ~0 for our own idle app.
            GpuVramUsedMb = Val(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            GpuVramTotalMb = Val(gpu, SensorType.SmallData, "GPU Memory Total")
                             ?? Sum(Val(gpu, SensorType.SmallData, "GPU Memory Used"), Val(gpu, SensorType.SmallData, "GPU Memory Free")),

            // Memory — physical vs commit/swap, kept distinct.
            RamUsedGb = Val(mem, SensorType.Data, "Memory Used"),
            RamTotalGb = Sum(Val(mem, SensorType.Data, "Memory Used"), Val(mem, SensorType.Data, "Memory Available")),
            RamLoad = Val(mem, SensorType.Load, "Memory"),
            SwapUsedGb = Val(virt, SensorType.Data, "Memory Used"),
            SwapTotalGb = Sum(Val(virt, SensorType.Data, "Memory Used"), Val(virt, SensorType.Data, "Memory Available")),
            SwapLoad = Val(virt, SensorType.Load, "Memory"),
        };
    }

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
