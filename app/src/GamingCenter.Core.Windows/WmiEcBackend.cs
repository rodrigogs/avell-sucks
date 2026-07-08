using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using GamingCenter.Core.Hardware;
using GamingCenter.Core.Models;

namespace GamingCenter.Core.Hardware;

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

    // Power-limit EC addresses — CONFIRMED from the decompiled OEM GamingCenter
    // (FanManagementPage2.SetPL1/2/4Value write WMIWriteECRAM(1923/1924/1925);
    // GetGamingPLDefaultValue reads 1840/1841/1842, Office reads 1844/1845/1846).
    // Byte watts. (Tau is NOT an EC register — the OEM sets it via Intel XTU
    // `-id 66`, so it's out of scope for the EC backend.)
    private const int ADDR_PL1_SETTING_VALUE = 1923; // 0x783
    private const int ADDR_PL2_SETTING_VALUE = 1924; // 0x784
    private const int ADDR_PL4_SETTING_VALUE = 1925; // 0x785

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

        var snapshot = await ReadSnapshotAsync([1873, 1858], cancellationToken).ConfigureAwait(false);
        var controlField = snapshot.Fields.FirstOrDefault(f => f.Address == 1873);
        if (controlField is not { Ok: true })
            return null;
        var raw = controlField.Value & 0xFF;

        var description = raw switch
        {
            0 => "Normal/Smart",
            64 => "FanBoost/Cold Mode",
            >= 128 and <= 132 => $"Custom Level {raw - 128}",
            160 => "Advanced Custom",
            _ => $"Unknown Control ({raw})"
        };
        var isAuto = raw is 0 or 64;

        return new FanMode(
            Source: nameof(WmiEcBackend),
            Timestamp: DateTimeOffset.UtcNow,
            RawValue: raw,
            IsAuto: isAuto,
            Description: description);
    }

    /// <summary>
    /// Reads the current CPU power limits from the confirmed EC registers
    /// (0x783/0x784/0x785 = PL1/PL2/PL4, byte watts). TauSeconds is reported as 0
    /// here because Tau lives in Intel XTU, not the EC.
    /// </summary>
    public async ValueTask<PowerProfileState?> ReadPowerProfileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var addresses = new[] { ADDR_PL1_SETTING_VALUE, ADDR_PL2_SETTING_VALUE, ADDR_PL4_SETTING_VALUE };
        var snapshot = await ReadSnapshotAsync(addresses, cancellationToken).ConfigureAwait(false);

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

    private static ManagementObject GetInstance()
    {
        using var searcher = new ManagementObjectSearcher(NamespacePath,
            $"SELECT * FROM {ClassName}");
        using var collection = searcher.Get();

        ManagementObject? fallback = null;
        foreach (ManagementObject instance in collection)
        {
            var name = instance["InstanceName"]?.ToString() ?? "";
            if (name.Contains("PNP0C14", StringComparison.OrdinalIgnoreCase))
                return instance;
            fallback ??= instance;
        }

        if (fallback is not null)
            return fallback;

        throw new InvalidOperationException(
            $"No {ClassName} instances available in {NamespacePath}.");
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
