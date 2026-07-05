using GamingCenter.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace GamingCenter.Core.Windows.Hardware;

public interface ITeRgbBackend
{
    Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default);
    Task<RgbState?> GetStateAsync(CancellationToken cancellationToken = default);
    Task<RgbEffectResult> ApplyEffectAsync(RgbEffect effect, CancellationToken cancellationToken = default);
    Task<RgbEffectResult> WriteZoneColorsAsync(int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default);
}

public record RgbEffectResult(bool Ok, string Error = null)
{
    public static readonly RgbEffectResult Success = new(true);
    public static RgbEffectResult Failure(string error) => new(false, error);
}

internal sealed class TeRgbBackend(HidInteropNative interop) : ITeRgbBackend
{
    private const ushort VendorId = HidNative.VendorId;
    private const ushort ProductId = HidNative.ProductId;
    private const int MaxColorsPerReport = 64;
    private const int MaxOutputReportLength = 65;

    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await interop.TryOpenKeyboardVidPidAsync(VendorId, ProductId, cancellationToken)
                .ConfigureAwait(false);
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
            using var device = await OpenDeviceAsync(cancellationToken).ConfigureAwait(false);
            if (device is null) return null;

            using var session = HidSession.CreateAsync(device, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            if (!session.IsSupported(HidNative.HIDP_REPORT_TYPE.Feature, 0x88))
                return null;

            var report = new byte[Interop.HidNative.FeatureReportMaxLength];
            Interop.HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                0x88,
                session.PreparsedData,
                report,
                (uint)report.Length);

            var status = HidNative.HidP_GetFeature(
                session.PreparsedData,
                report,
                ref report.Length);

            if (!HidSuccess(status, session.PreparsedData, report.Length))
                return null;

            var state = ParseStateReport(report);
            return state;
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
            using var device = await OpenDeviceAsync(cancellationToken).ConfigureAwait(false);
            if (device is null) return RgbEffectResult.Failure("HID device unavailable");

            using var session = HidSession.CreateAsync(device, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            if (!session.IsSupported(HidNative.HIDP_REPORT_TYPE.Feature, 0x08))
                return RgbEffectResult.Failure("Feature report 0x08 not supported");

            var payload = BuildEffectPayload(effect);
            var effectBuffer = new byte[Interop.HidNative.FeatureReportMaxLength];
            new Span<byte>(payload).CopyTo(new Span<byte>(effectBuffer, 1, payload.Length));
            effectBuffer[0] = 0x08;

            Interop.HidNative.HidP_InitializeReportForID(
                HidNative.HIDP_REPORT_TYPE.Feature,
                0x08,
                session.PreparsedData,
                effectBuffer,
                (uint)effectBuffer.Length);

            var status = HidNative.HidP_SetFeature(
                session.PreparsedData,
                effectBuffer,
                effectBuffer.Length);

            if (!HidSuccess(status, session.PreparsedData, effectBuffer.Length))
                return RgbEffectResult.Failure($"SetFeature 0x08 failed: {status}");

            if (effect.Save)
            {
                var savePayload = new byte[1] { 0x13 };
                var saveBuffer = new byte[Interop.HidNative.FeatureReportMaxLength];
                new Span<byte>(savePayload).CopyTo(new Span<byte>(saveBuffer, 1, savePayload.Length));
                saveBuffer[0] = 0x97;

                Interop.HidNative.HidP_InitializeReportForID(
                    HidNative.HIDP_REPORT_TYPE.Feature,
                    0x97,
                    session.PreparsedData,
                    saveBuffer,
                    (uint)saveBuffer.Length);

                var saveStatus = HidNative.HidP_SetFeature(
                    session.PreparsedData,
                    saveBuffer,
                    saveBuffer.Length);

                if (!HidSuccess(saveStatus, session.PreparsedData, saveBuffer.Length))
                    return RgbEffectResult.Failure($"SetFeature 0x97 save failed: {saveStatus}");
            }

            return RgbEffectResult.Success;
        }
        catch (Exception ex)
        {
            return RgbEffectResult.Failure(ex.Message);
        }
    }

    public async Task<RgbEffectResult> WriteZoneColorsAsync(int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        if (colors is null || colors.Count == 0)
            return RgbEffectResult.Failure("No colors provided");

        if (colors.Count > MaxColorsPerReport)
            return RgbEffectResult.Failure($"Max {MaxColorsPerReport} colors per zone supported.");

        try
        {
            using var device = await OpenDeviceAsync(cancellationToken).ConfigureAwait(false);
            if (device is null)
                return RgbEffectResult.Failure("HID device unavailable");

            using var stream = await device.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream is null)
                return RgbEffectResult.Failure("Failed to open HID stream");

            var report = new byte[MaxOutputReportLength];
            report[0] = 0x05;           // output report id
            report[1] = 0x12;           // load picture command
            report[2] = (byte)zoneIndex;

            // Find safe starting index based on layout cap when available; fallback to 0
            int startIndex = zoneIndex == 1 ? 0 : 0;
            int offset = 3;

            for (int i = 0; i < colors.Count && i < MaxColorsPerReport; i++)
            {
                if (offset + 3 > MaxOutputReportLength)
                    break;

                var c = colors[i];
                report[offset + 0] = (byte)(startIndex + i);
                report[offset + 1] = c.Red;
                report[offset + 2] = c.Green;
                report[offset + 3] = c.Blue;
                offset += 4;
            }

            await stream.WriteAsync(report, cancellationToken).ConfigureAwait(false);
            return RgbEffectResult.Success;
        }
        catch (Exception ex)
        {
            return RgbEffectResult.Failure(ex.Message);
        }
    }

    private async ValueTask<ITeRgbHidDevice?> OpenDeviceAsync(CancellationToken cancellationToken)
    {
        var path = await interop.TryOpenKeyboardVidPidAsync(VendorId, ProductId, cancellationToken);
        if (string.IsNullOrEmpty(path)) return null;
        return new TeRgbHidDevice(interop, path);
    }

    private static bool HidSuccess(HidNative.HIDP_STATUS status, nint preparsed, uint length)
    {
        return status == HidNative.HIDP_STATUS.SUCCESS;
    }

    private static RgbState ParseStateReport(byte[] report)
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
        var colors = effect.Colors.ToArray();
        if (colors.Length == 0) colors = [RgbColor.White];

        int index = 0;
        if (effect.Type == RgbEffectType.Static && colors.Length == 1)
            index = 0; // All LEDs one color
        else
            index = effect.Type == RgbEffectType.Breathing ? 0 : 1;

        var buffer = new byte[64];
        // Pack 4 bytes per color: index, R, G, B
        for (int i = 0; i < Math.Min(colors.Length, (buffer.Length - 1) / 4); i++)
        {
            int pos = 1 + i * 4;
            buffer[pos + 0] = (byte)(index + i);
            buffer[pos + 1] = colors[i].Red;
            buffer[pos + 2] = colors[i].Green;
            buffer[pos + 3] = colors[i].Blue;
        }

        // control byte = effect | speed<<4 | direction<<6
        buffer[0] = (byte)((int)effect.Type | ((int)effect.Speed << 4) | ((int)effect.Direction << 6));
        return buffer;
    }
}
