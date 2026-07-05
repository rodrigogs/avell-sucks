using GamingCenter.Core.Interfaces;
using GamingCenter.Core.Rgb;
using GamingCenter.Core.Interop;

namespace GamingCenter.Core.Hardware;

public sealed class ITERGBBackend(HidInteropNative interop) : IRGBBackend, System.IAsyncDisposable
{
    private const ushort VendorId = HidNative.VendorId;
    private const ushort ProductId = HidNative.ProductId;
    private const int MaxColors = 64;
    private bool _disposed;

    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await interop.TryOpenKeyboardVidPidAsync(VendorId, ProductId, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<RgbState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await interop.TryOpenKeyboardVidPidAsync(VendorId, ProductId, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(path)) return null;

            using var device = HidNative.OpenHandle(path);
            if (device.IsInvalid) return null;

            using var session = await HidSession.CreateAsync(device, cancellationToken)
                .ConfigureAwait(false);

            var report = new byte[HidNative.FeatureReportMaxLength];
            HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                0x88,
                session.PreparsedData,
                report,
                (uint)report.Length);

            uint length = (uint)report.Length;
            var status = HidNative.HidP_GetFeature(session.PreparsedData, report, ref length);
            if (!IsSuccess(status, session, (int)length)) return null;

            return ParseState(report);
        }
        catch
        {
            return null;
        }
    }

    public async Task<RgbEffectResult> ApplyEffectAsync(RgbEffect effect, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await interop.TryOpenKeyboardVidPidAsync(VendorId, ProductId, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(path)) return RgbEffectResult.Failure("HID device unavailable");

            using var device = HidNative.OpenHandle(path);
            if (device.IsInvalid) return RgbEffectResult.Failure("HID device unavailable");

            using var session = await HidSession.CreateAsync(device, cancellationToken)
                .ConfigureAwait(false);

            var payload = BuildEffectPayload(effect);
            var report = new byte[HidNative.FeatureReportMaxLength];
            new byte[1] { 0x08 }.CopyTo(report.AsSpan(0, 1));
            new Span<byte>(payload).CopyTo(report.AsSpan(1, payload.Length));

            HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                0x08,
                session.PreparsedData,
                report,
                (uint)report.Length);

            var status = HidNative.HidP_SetFeature(session.PreparsedData, report, (uint)report.Length);
            if (!IsSuccess(status, session, report.Length))
                return RgbEffectResult.Failure($"SetFeature 0x08 failed: 0x{status:X}");

            if (effect.Save)
            {
                var saveReport = new byte[HidNative.FeatureReportMaxLength];
                saveReport[0] = 0x97;
                HidNative.HidP_InitializeReportForID(
                    HidNative.HIDP_REPORT_TYPE.Feature,
                    0x97,
                    session.PreparsedData,
                    saveReport,
                    (uint)saveReport.Length);

                var saveStatus = HidNative.HidP_SetFeature(session.PreparsedData, saveReport, (uint)saveReport.Length);
                if (!IsSuccess(saveStatus, session, saveReport.Length))
                    return RgbEffectResult.Failure($"Save 0x97 failed: 0x{saveStatus:X}");
            }

            return RgbEffectResult.Success;
        }
        catch (Exception ex)
        {
            return RgbEffectResult.Failure(ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.CompletedTask;
    }

    private static bool IsSuccess(HidNative.HIDP_STATUS status, HidSession session, int length)
    {
        return status == HidNative.HIDP_STATUS.SUCCESS;
    }

    private static RgbState ParseState(byte[] report)
    {
        bool powerOn = report[1] != 0;
        byte brightness = report[2];
        var effect = (RgbEffectType)(report[3] & 0x0F);
        var direction = (RgbDirection)((report[4] >> 4) & 0x0F);
        var speed = (RgbSpeed)(report[4] & 0x0F);
        byte ledCount = report.Length > 5 ? report[5] : (byte)0;
        return new RgbState(powerOn, brightness, effect, direction, speed, ledCount);
    }

    private static byte[] BuildEffectPayload(RgbEffect effect)
    {
        var colors = effect.Colors.Count != 0 ? effect.Colors.ToArray() : [RgbColor.White];

        int index = effect.Type is RgbEffectType.Static && colors.Length == 1 ? 0 : effect.Type is RgbEffectType.Breathing ? 0 : 1;
        var buffer = new byte[64];
        for (int i = 0; i < colors.Length && i < (buffer.Length - 1) / 4; i++)
        {
            int pos = 1 + i * 4;
            buffer[pos + 0] = (byte)(index + i);
            buffer[pos + 1] = colors[i].Red;
            buffer[pos + 2] = colors[i].Green;
            buffer[pos + 3] = colors[i].Blue;
        }

        buffer[0] = (byte)((int)effect.Type | ((int)effect.Speed << 4) | ((int)effect.Direction << 4));
        return buffer;
    }
}
