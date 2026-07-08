using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;
using AvellSucks.Core.Platforms;

namespace AvellSucks.Core.Tests;

/// <summary>
/// In-memory EC reader/writer for tests. Holds a dictionary of register
/// values and records every read and write call.
/// </summary>
internal sealed class FakeEcBackend : IEcBackend, IEcWriter, IWriteAuditLog
{
    private readonly Dictionary<int, int> _registers = new();
    private readonly Dictionary<int, int> _readCounts = new();
    private readonly List<(int addr, int val)> _writes = new();
    private readonly List<EcWriteResult> _audit = new();

    /// <summary>
    /// What value the write read-back will return. Default: echo the
    /// requested value (simulates a successful write). Set to a different
    /// value to simulate read-back mismatch.
    /// </summary>
    public Func<int, int, int>? ReadBackOverride { get; set; }

    /// <summary>
    /// If set, WriteAsync throws to simulate a backend failure.
    /// </summary>
    public Exception? WriteException { get; set; }

    public void Seed(int address, int value) => _registers[address] = value;

    public IReadOnlyList<(int addr, int val)> Writes => _writes;
    public IReadOnlyList<EcWriteResult> AuditEntries => _audit;
    public int ReadCount(int address) => _readCounts.GetValueOrDefault(address);

    // --- IEcBackend ---

    public ValueTask<EcSnapshot> ReadSnapshotAsync(
        IReadOnlyList<int> addresses, CancellationToken cancellationToken = default)
    {
        var fields = new EcField[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            var addr = addresses[i];
            _readCounts[addr] = _readCounts.GetValueOrDefault(addr) + 1;
            var ok = _registers.TryGetValue(addr, out var val);
            fields[i] = new EcField(
                addr, $"0x{addr:X}",
                ok ? val : 0,
                ok ? $"0x{val:X}" : "0x0",
                ok, ok ? null : "not seeded");
        }

        return ValueTask.FromResult(new EcSnapshot(DateTimeOffset.UtcNow, fields));
    }

    public ValueTask<FanMode?> InterpretFanModeAsync(CancellationToken ct = default)
        => ValueTask.FromResult<FanMode?>(null);

    public ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken ct = default)
        => ValueTask.FromResult<PowerProfileState?>(null);

    // --- IEcWriter ---

    public ValueTask<EcField> WriteAsync(
        int address, int value, CancellationToken cancellationToken = default)
    {
        if (WriteException is not null)
            throw WriteException;

        _writes.Add((address, value));
        _registers[address] = value;

        var readBack = ReadBackOverride?.Invoke(address, value) ?? value;
        _registers[address] = readBack; // read-back reflects stored state

        return ValueTask.FromResult(new EcField(
            address, $"0x{address:X}",
            readBack, $"0x{readBack:X}",
            Ok: true, Error: null));
    }

    // --- IWriteAuditLog ---

    public ValueTask RecordAsync(EcWriteResult result, CancellationToken ct = default)
    {
        _audit.Add(result);
        return ValueTask.CompletedTask;
    }
}
