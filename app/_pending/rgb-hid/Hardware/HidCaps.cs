using AvellSucks.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace AvellSucks.Core.Windows.Hardware;

internal static class HidCaps
{
    public static bool Build(SafeFileHandle handle, out HidCapsPayload output)
    {
        output = default;

        try
        {
            if (!HidNative.HidD_GetPreparsedData(handle, out var preparsed))
                return false;

            try
            {
                nint capsPtr = HidNative.HidP_GetCaps(preparsed, out var caps);
                output.Usage = caps & 0xFFFF;
                output.UsagePage = (ushort)(caps >> 16);
                return true;
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }
        catch
        {
            return false;
        }
    }
}
