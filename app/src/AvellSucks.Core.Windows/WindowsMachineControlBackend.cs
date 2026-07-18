using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvellSucks.Core.Hardware;

/// <summary>
/// Windows implementation for model-specific PnP controls, panel brightness and
/// interactive display power. Device matching is based on IDs verified on the
/// Avell G1555, never localized friendly names.
/// </summary>
public sealed class WindowsMachineControlBackend : IPlatformMachineControlBackend
{
    private const uint CrSuccess = 0;
    private const uint CmLocateDevNodeNormal = 0;
    private const uint CmDisableUiNotOk = 0;

    private const uint WmSysCommand = 0x0112;
    private const uint ScMonitorPower = 0xF170;
    private const int MonitorPowerOff = 2;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly nint HwndBroadcast = new(0xFFFF);

    private const string IntelWifiPrefix = @"PCI\VEN_8086&DEV_A370";
    private const string IntelBluetoothPrefix = @"USB\VID_8087&PID_0AAA";
    private const string TouchpadPrefix = @"ACPI\UNIW0001\";
    private const string WebcamPrefix = @"USB\VID_5986&PID_069E&MI_00\";

    public ValueTask<PlatformMachineControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => new(Task.Run(ReadStatusBlocking, cancellationToken));

    public async ValueTask<PlatformMutationResult> ReconcileWirelessRadiosAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            var scan = await RunPnPScanAsync(cancellationToken).ConfigureAwait(false);
            if (!scan)
                return PlatformMutationResult.Failure("pnputil device rescan failed.");

            if (enabled)
            {
                // The Wi-Fi function can survive an EC-off cycle as Code 22. Bluetooth
                // usually disappears entirely and re-enumerates after the trigger.
                foreach (var device in ReadPnpDevices().Where(d =>
                             StartsWith(d.InstanceId, IntelWifiPrefix) ||
                             StartsWith(d.InstanceId, IntelBluetoothPrefix)))
                {
                    if (device.ProblemCode == 22)
                    {
                        var result = SetDevNodeEnabled(device.InstanceId, enabled: true);
                        if (result != CrSuccess)
                            return PlatformMutationResult.Failure(
                                $"Could not enable {device.InstanceId}; Configuration Manager returned 0x{result:X}.");
                    }
                }

                await RunPnPScanAsync(cancellationToken).ConfigureAwait(false);
            }

            return PlatformMutationResult.Success("Windows device tree reconciled after the EC radio transition.");
        }
        catch (Exception ex)
        {
            return PlatformMutationResult.Failure($"Radio reconciliation failed: {ex.Message}");
        }
    }

    public ValueTask<PlatformMutationResult> SetTouchpadEnabledAsync(
        bool enabled, CancellationToken cancellationToken = default)
        => SetPnpEnabledAsync(IsTargetTouchpad, "touchpad", enabled, cancellationToken);

    public ValueTask<PlatformMutationResult> SetWebcamEnabledAsync(
        bool enabled, CancellationToken cancellationToken = default)
        => SetPnpEnabledAsync(IsTargetWebcam, "webcam", enabled, cancellationToken);

    public ValueTask<PlatformMutationResult> SetBrightnessAsync(
        byte percent, CancellationToken cancellationToken = default)
        => new(Task.Run(() => SetBrightnessBlocking(percent), cancellationToken));

    public ValueTask<PlatformMutationResult> TurnOffDisplayAsync(
        CancellationToken cancellationToken = default)
        => new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Environment.UserInteractive)
                return PlatformMutationResult.Failure(
                    "Display power control requires an interactive user session; the Windows service runs in session 0.");

            var sent = SendMessageTimeout(
                HwndBroadcast, WmSysCommand, new nint(ScMonitorPower),
                new nint(MonitorPowerOff), SmtoAbortIfHung, 2000, out _);
            return sent != nint.Zero
                ? PlatformMutationResult.Accepted(
                    "Monitor power-off request dispatched; physical panel state cannot be queried reliably.")
                : PlatformMutationResult.Failure(
                    $"SC_MONITORPOWER dispatch failed (Win32 {Marshal.GetLastWin32Error()}).");
        }, cancellationToken));

    public static bool IsTargetTouchpad(string instanceId)
        => StartsWith(instanceId, TouchpadPrefix);

    public static bool IsTargetWebcam(string instanceId)
        => StartsWith(instanceId, WebcamPrefix);

    public static bool? EnabledFromProblemCode(int problemCode) => problemCode switch
    {
        0 => true,
        22 => false,
        _ => null,
    };

    public static bool IsSupportedMachineIdentity(string? manufacturer, string? model)
        => manufacturer?.Contains("Avell", StringComparison.OrdinalIgnoreCase) == true
           && (string.Equals(model?.Trim(), "1555", StringComparison.OrdinalIgnoreCase)
               || string.Equals(model?.Trim(), "G1555", StringComparison.OrdinalIgnoreCase));

    public static string PnPUtilPath
        => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    private static PlatformMachineControlStatus ReadStatusBlocking()
    {
        var errors = new List<string>();
        var supportedMachine = false;
        try
        {
            var (manufacturer, model) = ReadSystemIdentity();
            supportedMachine = IsSupportedMachineIdentity(manufacturer, model);
        }
        catch (Exception ex)
        {
            errors.Add($"Machine identity read failed: {ex.Message}");
        }

        List<PnpDevice> devices = [];
        try
        {
            devices = ReadPnpDevices();
        }
        catch (Exception ex)
        {
            errors.Add($"PnP status read failed: {ex.Message}");
        }

        byte? brightness = null;
        try
        {
            brightness = ReadBrightnessBlocking();
        }
        catch (Exception ex)
        {
            errors.Add($"Brightness status read failed: {ex.Message}");
        }

        var touchpad = devices.FirstOrDefault(d => IsTargetTouchpad(d.InstanceId));
        var webcam = devices.FirstOrDefault(d => IsTargetWebcam(d.InstanceId));
        return new PlatformMachineControlStatus(
            SupportedMachine: supportedMachine,
            WifiPresent: devices.Any(d => StartsWith(d.InstanceId, IntelWifiPrefix) && d.ProblemCode == 0),
            BluetoothPresent: devices.Any(d => StartsWith(d.InstanceId, IntelBluetoothPrefix) && d.ProblemCode == 0),
            TouchpadEnabled: touchpad is null ? null : EnabledFromProblemCode(touchpad.ProblemCode),
            WebcamEnabled: webcam is null ? null : EnabledFromProblemCode(webcam.ProblemCode),
            BrightnessPercent: brightness,
            DisplayPowerControlAvailable: Environment.UserInteractive,
            Error: errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static async ValueTask<PlatformMutationResult> SetPnpEnabledAsync(
        Func<string, bool> selector, string label, bool enabled, CancellationToken cancellationToken)
    {
        try
        {
            var device = await Task.Run(
                () => ReadPnpDevices().FirstOrDefault(d => selector(d.InstanceId)), cancellationToken)
                .ConfigureAwait(false);
            if (device is null)
                return PlatformMutationResult.Failure($"Verified {label} PnP device was not found.");

            var result = await Task.Run(
                () => SetDevNodeEnabled(device.InstanceId, enabled), cancellationToken)
                .ConfigureAwait(false);
            if (result != CrSuccess)
                return PlatformMutationResult.Failure(
                    $"Configuration Manager returned 0x{result:X} while trying to {(enabled ? "enable" : "disable")} {label}.");

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var after = await Task.Run(
                () => ReadPnpDevices().FirstOrDefault(d => selector(d.InstanceId)), cancellationToken)
                .ConfigureAwait(false);
            var observed = after is null ? null : EnabledFromProblemCode(after.ProblemCode);
            return observed == enabled
                ? PlatformMutationResult.Success()
                : PlatformMutationResult.Failure(
                    $"{label} PnP state did not verify after the request (observed: {observed?.ToString() ?? "unknown"}).");
        }
        catch (Exception ex)
        {
            return PlatformMutationResult.Failure($"{label} control failed: {ex.Message}");
        }
    }

    private static PlatformMutationResult SetBrightnessBlocking(byte percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
            using var collection = searcher.Get();
            var method = collection.Cast<ManagementObject>().FirstOrDefault();
            if (method is null)
                return PlatformMutationResult.Failure("No active WmiMonitorBrightnessMethods instance was found.");

            using (method)
            {
                var input = method.GetMethodParameters("WmiSetBrightness");
                input["Timeout"] = 0u;
                input["Brightness"] = percent;
                method.InvokeMethod("WmiSetBrightness", input, null);
            }

            Thread.Sleep(250);
            var observed = ReadBrightnessBlocking();
            return observed == percent
                ? PlatformMutationResult.Success()
                : PlatformMutationResult.Failure(
                    $"Brightness read-back mismatch: requested {percent}, observed {observed?.ToString() ?? "unknown"}.");
        }
        catch (Exception ex)
        {
            return PlatformMutationResult.Failure($"Brightness control failed: {ex.Message}");
        }
    }

    private static byte? ReadBrightnessBlocking()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\WMI", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                if (item["CurrentBrightness"] is byte value) return value;
                if (item["CurrentBrightness"] is uint wide) return (byte)wide;
            }
        }
        return null;
    }

    private static List<PnpDevice> ReadPnpDevices()
    {
        var devices = new List<PnpDevice>();
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2", "SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var id = item["DeviceID"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var problem = Convert.ToInt32(item["ConfigManagerErrorCode"] ?? -1);
                devices.Add(new PnpDevice(id, problem));
            }
        }
        return devices;
    }

    private static (string? Manufacturer, string? Model) ReadSystemIdentity()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection)
        {
            using (item)
                return (item["Manufacturer"]?.ToString(), item["Model"]?.ToString());
        }
        return (null, null);
    }

    private static uint SetDevNodeEnabled(string instanceId, bool enabled)
    {
        var locate = CM_Locate_DevNodeW(out var devInst, instanceId, CmLocateDevNodeNormal);
        if (locate != CrSuccess) return locate;
        return enabled
            ? CM_Enable_DevNode(devInst, 0)
            : CM_Disable_DevNode(devInst, CmDisableUiNotOk);
    }

    private static async Task<bool> RunPnPScanAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(PnPUtilPath, "/scan-devices")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        if (!process.Start()) return false;
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
    }

    private static bool StartsWith(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private sealed record PnpDevice(string InstanceId, int ProblemCode);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam,
        uint flags, uint timeout, out nint result);
}
