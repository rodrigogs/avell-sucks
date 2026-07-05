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

    public void Dispose()
    {
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
