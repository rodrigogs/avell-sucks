using System.Runtime.InteropServices;

namespace GamingCenter.Core.Interop;

// Native HID interop for ITE USB RGB keyboard control.

internal static partial class HidNative
{
    public const ushort VendorId = 0x0489;
    public const ushort ProductId = 0xCE40;
    public const int FeatureReportMaxLength = 64;

    [StructLayout(LayoutKind.Sequential)]
    public struct ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    public enum REPORT_TYPE : uint
    {
        Input = 0,
        Output = 1,
        Feature = 2
    }

    public enum STATUS : uint
    {
        SUCCESS = 0x110000,
        INVALID_PREPARSED_DATA = 0x110001,
        INVALID_REPORT_TYPE = 0x110002,
        INVALID_REPORT_LENGTH = 0x110003,
        USAGE_NOT_FOUND = 0x110004,
        VALUE_OUT_OF_RANGE = 0x110005,
        BAD_LOG_PHY_VALUES = 0x110006,
        BUFFER_TOO_SMALL = 0x110007,
        INTERNAL_ERROR = 0x110008,
        I8042_TRANS_UNKNOWN = 0x110009,
        INCOMPATIBLE_REPORT_ID = 0x11000A,
        NOT_VALUE_ARRAY = 0x11000B,
        IS_VALUE_ARRAY = 0x11000C,
        DATA_INDEX_NOT_FOUND = 0x11000D,
        DATA_INDEX_OUT_OF_RANGE = 0x11000E,
        BUTTON_NOT_PRESSED = 0x11000F,
        REPORT_DOES_NOT_EXIST = 0x110010,
        NOT_IMPLEMENTED = 0x110020,
        NULL = 0x110021,
        REPTYPEMISMATCH = 0x110025,
        INVALID_CAPS = 0x110029,
    }

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_GetAttributes(
        Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject,
        ref ATTRIBUTES attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_GetPreparsedData(
        Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject,
        out IntPtr preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [LibraryImport("hid.dll")]
    public static partial nint HidP_GetCaps(IntPtr preparsedData, out ushort caps);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial STATUS HidP_InitializeReportForID(
        REPORT_TYPE reportType,
        byte reportID,
        IntPtr preparsedData,
        byte[] reportBuffer,
        uint reportBufferLength);

    [LibraryImport("hid.dll")]
    public static partial STATUS HidP_GetFeature(
        IntPtr preparsedData,
        byte[] reportBuffer,
        ref uint reportBufferLength);

    [LibraryImport("hid.dll")]
    public static partial STATUS HidP_SetFeature(
        IntPtr preparsedData,
        byte[] reportBuffer,
        uint reportBufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_GetSerialNumberString(
        Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject,
        byte[] buffer,
        uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial bool HidD_GetManufacturerString(
        Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject,
        char[] buffer,
        uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial bool HidD_GetProductString(
        Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject,
        char[] buffer,
        uint bufferLength);
}
