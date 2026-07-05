using GamingCenter.Core.Interop;
using GamingCenter.Core.Rgb;
using Microsoft.Win32.SafeHandles;

namespace GamingCenter.Core.Windows.Hardware;

public sealed class ITEHidDeviceProvider : IHidDeviceProvider
{
    public Task<string?> OpenKeyboardAsync(CancellationToken cancellationToken = default)
    {
        if (HidInteropNative.TryOpenKeyboardVidPid(HidNative.VendorId, HidNative.ProductId, out var path))
            return Task.FromResult<string?>(path);

        return Task.FromResult<string?>(null);
    }

    public Task<byte[]?> GetFeatureAsync(string devicePath, byte reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handle = HidInteropNative.OpenHandle(devicePath);
            if (handle.IsInvalid) return Task.FromResult<byte[]?>(null);

            using var session = HidSession.CreateAsync(handle, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            var report = new byte[Interop.HidNative.FeatureReportMaxLength];
            HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                reportId,
                session.PreparsedData,
                report,
                (uint)report.Length);
            uint length = (uint)report.Length;
            var status = HidNative.HidP_GetFeature(session.PreparsedData, report, ref length);
            return HidSuccess(status, session.PreparsedData, (int)length)
                ? Task.FromResult<byte[]?>(report)
                : Task.FromResult<byte[]?>(null);
        }
        catch
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task SetFeatureAsync(string devicePath, byte reportId, byte[] payload, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handle = HidInteropNative.OpenHandle(devicePath);
            if (handle.IsInvalid)
            {
                return Task.CompletedTask;
            }

            using var session = HidSession.CreateAsync(handle, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            var report = new byte[Interop.HidNative.FeatureReportMaxLength];
            new Span<byte>(payload).CopyTo(new Span<byte>(report, 1, payload.Length));
            report[0] = reportId;

            HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                reportId,
                session.PreparsedData,
                report,
                (uint)report.Length);

            var status = HidNative.HidP_SetFeature(
                session.PreparsedData,
                report,
                (uint)report.Length);

            HidSuccess(status, session.PreparsedData, report.Length);
        }
        catch
        {
            // swallow — write best effort.
        }

        return Task.CompletedTask;
    }

    public Task<IRGBHidDeviceStream?> OpenStreamAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var handle = HidInteropNative.OpenHandle(devicePath);
            if (handle.IsInvalid)
                return Task.FromResult<IRGBHidDeviceStream?>(null);

            return Task.FromResult<IRGBHidDeviceStream?>(
                new HidDeviceStream(handle, cancellationToken));
        }
        catch
        {
            return Task.FromResult<IRGBHidDeviceStream?>(null);
        }
    }

    public bool TryOpenKeyboard(out string? devicePath, CancellationToken cancellationToken = default)
        => HidInteropNative.TryOpenKeyboardVidPid(HidNative.VendorId, HidNative.ProductId, out devicePath);

    public IReadOnlyList<string> GetAvailableUsagePages(CancellationToken cancellationToken = default)
    {
        return HidInteropNative.EnumerateKeyboardDevicePaths(HidNative.VendorId, HidNative.ProductId)
            .Select(p => p.UsagePage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HidSuccess(HidNative.HIDP_STATUS status, nint preparsed, int length)
    {
        return status == HidNative.HIDP_STATUS.SUCCESS;
    }
}
