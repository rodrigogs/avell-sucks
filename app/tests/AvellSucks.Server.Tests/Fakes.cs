using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;
using AvellSucks.Core.Platforms;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Simple stub EC backend for controller testing — no register state,
/// just returns whatever mode was seeded. Distinct from Core.Tests'
/// FakeEcBackend which has full register/audit state.
/// </summary>
internal sealed class StubEcBackend : IEcBackend, IEcWriter
{
    private FanMode? _mode;
    private readonly Dictionary<int, int> _registers = new();

    public List<(int Address, int Value)> Writes { get; } = new();

    public void SetMode(FanMode? mode) => _mode = mode;

    public void SeedCurve(IReadOnlyList<int> values)
    {
        var addresses = new[] { 1859, 1860, 1861, 1862, 1863 };
        for (var i = 0; i < addresses.Length && i < values.Count; i++)
            _registers[addresses[i]] = values[i];
    }

    public ValueTask<EcSnapshot> ReadSnapshotAsync(IReadOnlyList<int> addresses, CancellationToken ct = default)
    {
        var fields = new EcField[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            var value = _registers.GetValueOrDefault(addresses[i], 0);
            fields[i] = new EcField(addresses[i], $"0x{addresses[i]:X}", value, $"0x{value:X}", true, null);
        }
        return ValueTask.FromResult(new EcSnapshot(System.DateTimeOffset.UtcNow, fields));
    }

    public ValueTask<FanMode?> InterpretFanModeAsync(CancellationToken ct = default)
        => ValueTask.FromResult(_mode);

    public ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken ct = default)
        => ValueTask.FromResult<PowerProfileState?>(null);

    public ValueTask<EcField> WriteAsync(int address, int value, CancellationToken ct = default)
    {
        _registers[address] = value;
        Writes.Add((address, value));
        return ValueTask.FromResult(new EcField(address, $"0x{address:X}", value, $"0x{value:X}", true, null));
    }
}

internal sealed class ListAuditLog : IWriteAuditLog
{
    public List<EcWriteResult> Entries { get; } = new();
    public ValueTask RecordAsync(EcWriteResult result, CancellationToken ct = default)
    {
        Entries.Add(result);
        return ValueTask.CompletedTask;
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public System.IDisposable? OnChange(System.Action<T, string?> listener) => null;
}

// Minimal IOptionsMonitor<AuthenticationSchemeOptions> for the handler base ctor.
internal sealed class OptionsMonitorStub : Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    private readonly Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions _o = new();
    public Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions CurrentValue => _o;
    public Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions Get(string? name) => _o;
    public System.IDisposable? OnChange(System.Action<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, string?> listener) => null;
}
