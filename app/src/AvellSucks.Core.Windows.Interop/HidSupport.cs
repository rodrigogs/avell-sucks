using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AvellSucks.Core.Windows.Interop;

public static partial class ITEHidInterop
{
    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_GetAttributes(
        SafeFileHandle HidDeviceObject,
        ref HIDD_ATTRIBUTES Attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_GetPreparsedData(
        SafeFileHandle HidDeviceObject,
        out IntPtr PreparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    public static partial SafeFileHandle SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    public static partial bool SetupDiEnumDeviceInterfaces(
        SafeFileHandle DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    public static partial bool SetupDiDestroyDeviceInfoList(SafeFileHandle DeviceInfo);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial nint HidP_GetCaps(IntPtr PreparsedData, out ushort Caps);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial HIDP_STATUS HidP_InitializeReportForID(
        HIDP_REPORT_TYPE ReportType,
        byte ReportID,
        IntPtr PreparsedData,
        byte[] ReportBuffer,
        uint ReportBufferLength);

    [LibraryImport("hid.dll")]
    public static partial HIDP_STATUS HidP_GetFeature(
        IntPtr PreparsedData,
        byte[] ReportBuffer,
        ref uint ReportBufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    public static partial HIDP_STATUS HidP_SetFeature(
        IntPtr PreparsedData,
        byte[] ReportBuffer,
        uint ReportBufferLength);
}
