using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;

var backend = new FakeEcBackend();
backend.Seed(1857, 0x11);
backend.Seed(1858, 0xBE);
backend.Seed(1859, 0x22);
backend.Seed(1860, 0x33);
backend.Seed(1861, 0x44);

var addresses = new[] { 1857, 1858, 1859, 1860, 1861 };

Console.WriteLine("EC read-only smoke");
Console.WriteLine($"Backend: {backend.GetType().FullName}");
Console.WriteLine($"Addresses: {string.Join(", ", addresses.Select(a => $"0x{a:X}"))}");
Console.WriteLine();

var snapshot = await backend.ReadSnapshotAsync(addresses);

Console.WriteLine($"Timestamp: {snapshot.Timestamp:o}");
foreach (var field in snapshot.Fields)
{
    Console.WriteLine(field.Ok
        ? $"{field.HexAddress}: {field.Value} ({field.ValueHex})"
        : $"{field.HexAddress}: ERROR - {field.Error}");
}

Console.WriteLine();
var mode = await backend.InterpretFanModeAsync();
Console.WriteLine(mode is { } m
    ? $"FanMode: {m.Description}; Raw={m.RawValue}; IsAuto={m.IsAuto}"
    : "FanMode: unavailable");

if (snapshot.Fields.All(f => f.Ok) && mode is not null)
{
    Console.WriteLine("Read-only smoke result: PASS");
    return 0;
}

Console.WriteLine("Read-only smoke result: DEGRADED");
return 2;

internal sealed class FakeEcBackend : IEcBackend, IAsyncDisposable
{
    private readonly Dictionary<int, int> _registers = new();

    public void Seed(int address, int value) => _registers[address] = value;

    public ValueTask<EcSnapshot> ReadSnapshotAsync(IReadOnlyList<int> addresses, CancellationToken cancellationToken = default)
    {
        var fields = new EcField[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            var addr = addresses[i];
            var ok = _registers.TryGetValue(addr, out var val);
            fields[i] = new EcField(
                addr, $"0x{addr:X}",
                ok ? val : 0,
                ok ? $"0x{val:X}" : "0x0",
                ok, ok ? null : "not seeded");
        }
        return ValueTask.FromResult(new EcSnapshot(DateTimeOffset.UtcNow, fields));
    }

    public ValueTask<FanMode?> InterpretFanModeAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<FanMode?>(null);

    public ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<PowerProfileState?>(null);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
