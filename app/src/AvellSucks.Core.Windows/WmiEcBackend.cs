using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;

namespace AvellSucks.Core.Hardware;

/// <summary>
/// Windows backend using root\WMI AcpiTest_MULong. Implements both read
/// (<see cref="IEcBackend"/>) and write (<see cref="IEcWriter"/>) via the
/// WMI <c>GetSetULong</c> method.
/// </summary>
/// <remarks>
/// WMI method encoding (from reverse-engineered WMIEC.cs):
/// <code>
///   // Read:  data = 0x100_0000_0000 | addr       (2^40 + addr)
///   // Write: data = (value &lt;&lt; 16) | (0x100_0000_0000 | addr)
///   var p = instance.GetMethodParameters("GetSetULong");
///   p["Data"] = data;
///   var r = instance.InvokeMethod("GetSetULong", p, null);
///   return r["Return"];  // read-back value
/// </code>
/// </remarks>
public sealed class WmiEcBackend : IEcBackend, IEcWriter, IAsyncDisposable
{
    private const string NamespacePath = @"root\WMI";
    private const string ClassName = "AcpiTest_MULong";
    private const ulong ReadFlag = 0x100_0000_0000UL; // 2^40

    // Power-limit EC addresses come from the shared PowerRegisters source so the
    // backend, the WMI power service, the API controller and the write allowlist
    // never disagree. (Tau is NOT an EC register — the OEM sets it via Intel XTU
    // `-id 66`, so it's out of scope for the EC backend.)

    public async ValueTask<EcSnapshot> ReadSnapshotAsync(
        IReadOnlyList<int> addresses, CancellationToken cancellationToken = default)
    {
        if (addresses is null || addresses.Count == 0)
            throw new ArgumentException("At least one address is required.", nameof(addresses));

        var fields = new EcField[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fields[i] = await Task.Run(() => ReadFieldBlocking(addresses[i]), cancellationToken)
                .ConfigureAwait(false);
        }

        return new EcSnapshot(DateTimeOffset.UtcNow, fields);
    }

    public async ValueTask<FanMode?> InterpretFanModeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Only the control byte (0x751) is needed to interpret the mode; the
        // second address was read but never used. One WMI read per call.
        var snapshot = await ReadSnapshotAsync([1873], cancellationToken).ConfigureAwait(false);
        var controlField = snapshot.Fields.FirstOrDefault(f => f.Address == 1873);
        if (controlField is not { Ok: true })
            return null;
        var raw = controlField.Value & 0xFF;

        return new FanMode(
            Source: nameof(WmiEcBackend),
            Timestamp: DateTimeOffset.UtcNow,
            RawValue: raw,
            IsAuto: FanModeMap.IsAutoManaged(raw),
            // Was ">= 128 and <= 132" — an off-by-one that mislabelled L5 (133) and
            // wrongly matched 128. FanModeMap.Describe handles 129..133 correctly.
            Description: FanModeMap.Describe(raw));
    }

    /// <summary>
    /// Reads the current CPU power limits from the confirmed EC registers
    /// (0x783/0x784/0x785 = PL1/PL2/PL4, byte watts). TauSeconds is reported as 0
    /// here because Tau lives in Intel XTU, not the EC.
    /// </summary>
    public async ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await ReadSnapshotAsync(PowerRegisters.Setting, cancellationToken).ConfigureAwait(false);

        if (snapshot.Fields.Count < 3)
            return new PowerProfileState(DateTimeOffset.UtcNow, 0, 0, 0, false, "Incomplete power-register snapshot.");

        if (!snapshot.Fields.All(f => f.Ok))
            return new PowerProfileState(DateTimeOffset.UtcNow, 0, 0, 0, true, "Partial read: some power registers returned errors.");

        // Byte watts, confirmed against the OEM SetPL*Value write path.
        var pl1 = snapshot.Fields[0].Value;
        var pl2 = snapshot.Fields[1].Value;

        return new PowerProfileState(
            Timestamp: snapshot.Timestamp,
            Pl1Watts: pl1,
            Pl2Watts: pl2,
            TauSeconds: 0, // Tau is an XTU setting, not an EC register.
            Supported: true,
            Error: null);
    }

    // --- IEcWriter ---

    public async ValueTask<EcField> WriteAsync(
        int address, int value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => WriteBlocking(address, value), cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadFieldBlocking(address), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // --- WMI plumbing ---

    // The resolved WMI object path, cached after the first lookup. Enumerating the
    // class (ManagementObjectSearcher + .Get()) is expensive and was previously
    // done on EVERY read/write — a full WMI query per EC byte, several times a
    // second across the monitors. We resolve the instance path once, then build a
    // lightweight ManagementObject directly from it (like the OEM, which hardcodes
    // AcpiTest_MULong.InstanceName='ACPI\PNP0C14\1_1').
    private static string? s_instancePath;
    private static readonly object s_pathLock = new();

    private static ManagementObject GetInstance()
    {
        var path = s_instancePath;
        if (path is not null)
            return new ManagementObject(NamespacePath, path, null);

        lock (s_pathLock)
        {
            if (s_instancePath is not null)
                return new ManagementObject(NamespacePath, s_instancePath, null);

            using var searcher = new ManagementObjectSearcher(NamespacePath,
                $"SELECT * FROM {ClassName}");
            using var collection = searcher.Get();

            ManagementObject? fallback = null;
            foreach (ManagementObject instance in collection)
            {
                var name = instance["InstanceName"]?.ToString() ?? "";
                if (name.Contains("PNP0C14", StringComparison.OrdinalIgnoreCase))
                {
                    s_instancePath = $"{ClassName}.InstanceName=\"{name.Replace("\\", "\\\\")}\"";
                    return instance;
                }
                fallback ??= instance;
            }

            if (fallback is not null)
            {
                var fname = fallback["InstanceName"]?.ToString() ?? "";
                s_instancePath = $"{ClassName}.InstanceName=\"{fname.Replace("\\", "\\\\")}\"";
                return fallback;
            }

            throw new InvalidOperationException(
                $"No {ClassName} instances available in {NamespacePath}.");
        }
    }

    private static ulong PackRead(int address) => ReadFlag | (uint)address;

    // Write encoding from the decompiled OEM WMIEC.WMIWriteECRAM:
    //   Value <<= 16;  Data = Value + Addr;   // (value<<16) + addr, NO read flag.
    // Including ReadFlag (2^40) here makes the EC ignore the write (read-back
    // stays unchanged), so the write must NOT carry it.
    private static ulong PackWrite(int address, int value) =>
        ((ulong)(uint)value << 16) + (uint)address;

    private static ulong? InvokeGetSet(ManagementObject instance, ulong data)
    {
        var parameters = instance.GetMethodParameters("GetSetULong");
        parameters["Data"] = data;
        var result = instance.InvokeMethod("GetSetULong", parameters, null);
        return result?["Return"] switch
        {
            uint v => v,
            ulong v => v,
            int v => (uint)v,
            long v => (ulong)v,
            _ => null
        };
    }

    private static EcField ReadFieldBlocking(int address)
    {
        try
        {
            using var instance = GetInstance();
            var readBack = InvokeGetSet(instance, PackRead(address));

            if (readBack is { } rawUlong)
            {
                var value = (int)(rawUlong & 0xFF);
                return new EcField(
                    address, $"0x{address:X}",
                    value, $"0x{value:X}",
                    Ok: true, Error: null);
            }

            return new EcField(
                address, $"0x{address:X}",
                0, "0x0", Ok: false,
                Error: "WMI GetSetULong returned no value.");
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            return new EcField(
                address, $"0x{address:X}",
                0, "0x0", Ok: false,
                Error: $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WriteBlocking(int address, int value)
    {
        try
        {
            using var instance = GetInstance();
            InvokeGetSet(instance, PackWrite(address, value));
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"WMI GetSetULong write failed for addr 0x{address:X}, value 0x{value:X}: {ex.Message}", ex);
        }
    }
}
