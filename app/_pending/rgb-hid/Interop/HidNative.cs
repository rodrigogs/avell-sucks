// Native HID interop for ITE USB RGB keyboard control.
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace AvellSucks.Core.Windows.Interop;

internal static class HidNative
{
    public const ushort UsagePage = 0xFF00;
    public const ushort Usage = 0x01;
    public const ushort VendorId = 0x0489;
    public const ushort ProductId = 0xCE40;

    public const int FeatureReportMaxLength = 64;

    public const string HidGuidString = "{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [Flags]
    public enum DIGCF : uint
    {
        DEFAULT = 0x00000001,
        PRESENT = 0x00000002,
        ALLCLASSES = 0x00000004,
        PROFILE = 0x00000008,
        DEVICEINTERFACE = 0x00000010,
    }

    [Flags]
    public enum HIDP_REPORT_TYPE : uint
    {
        Input = 0,
        Output = 1,
        Feature = 2
    }

    public enum HIDP_STATUS : uint
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
        REPOR_IDS_MISMATCH = 0x110022,
        EXCEED_MAXIMUM_USAGES = 0x110024,
        REPTYPEMISMATCH = 0x110025,
        NULLIDFULLINFOSET = 0x110027,
        INCOMPATIBLE_VAR_PACKET = 0x110028,
        INVALID_CAPS = 0x110029,
        INVALID_EXCLUDED_USAGE = 0x11002A,
        INVALID_LINK_COLLECTION = 0x11002B
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public uint cbSize;
        public _DEVICE_PATH DevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Size = 4096)]
    public struct _DEVICE_PATH
    {
        [FieldOffset(0)] public fixed char Path[4096 / sizeof(char)];
    }

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(
        SafeFileHandle HidDeviceObject,
        ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(
        SafeFileHandle HidDeviceObject,
        out nint PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(nint PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern nint HidP_GetCaps(nint PreparsedData, out ushort Caps);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern HIDP_STATUS HidP_InitializeReportForID(
        HIDP_REPORT_TYPE ReportType,
        byte ReportID,
        nint PreparsedData,
        byte[] ReportBuffer,
        uint ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern HIDP_STATUS HidP_GetFeature(
        nint PreparsedData,
        byte[] ReportBuffer,
        ref uint ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern HIDP_STATUS HidP_SetFeature(
        nint PreparsedData,
        byte[] ReportBuffer,
        uint ReportBufferLength);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern SafeFileHandle SetupDiGetClassDevs(
        ref Guid ClassGuid,
        nint Enumerator,
        nint hwndParent,
        DIGCF Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        SafeFileHandle DeviceInfoSet,
        nint DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        SafeFileHandle DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        nint DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(SafeFileHandle DeviceInfo);
}
