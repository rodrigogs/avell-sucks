using AvellSucks.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace AvellSucks.Core.Windows.Hardware;

internal ref struct HidSession
{
    public nint PreparsedData { get; }
    public bool Owned { get; }
    public int ReportLength { get; }

    private HidSession(nint preparsed, bool owned, int reportLength)
    {
        PreparsedData = preparsed;
        Owned = owned;
        ReportLength = reportLength;
    }

    public static async ValueTask<HidSession> CreateAsync(SafeFileHandle handle, CancellationToken cancellationToken = default)
    {
        if (!HidNative.HidD_GetPreparsedData(handle, out var preparsed))
            throw new InvalidOperationException("HidD_GetPreparsedData failed.");

        try
        {
            if (HidNative.HidP_GetCaps(preparsed, out var caps) == 0)
                throw new InvalidOperationException("HidP_GetCaps failed.");

            ushort usagePage = (ushort)(caps >> 16);
            ushort usage = (ushort)(caps & 0xFFFF);
            int reportLength = ushort.MaxValue;

            await Task.Yield(); // preserve async shape
            return new HidSession(preparsed, owned: true, reportLength);
        }
        catch
        {
            Release();
            throw;
        }

        void Release()
        {
            if (preparsed != nint.Zero)
                HidNative.HidD_FreePreparsedData(preparsed);
        }
    }

    public bool IsSupported(HidNative.HIDP_REPORT_TYPE type, byte reportId)
    {
        if (PreparsedData == nint.Zero) return false;
        var status = HidNative.HidP_InitializeReportForID(
            type,
            reportId,
            PreparsedData,
            new byte[Interop.HidNative.FeatureReportMaxLength],
            (uint)Interop.HidNative.FeatureReportMaxLength);
        return status == HidNative.HIDP_STATUS.SUCCESS;
    }

    public void Dispose()
    {
        if (Owned && PreparsedData != nint.Zero)
            HidNative.HidD_FreePreparsedData(PreparsedData);
    }
}
