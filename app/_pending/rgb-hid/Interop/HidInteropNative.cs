// Thin interop wrapper around HID SetupAPI + HidD/HidP interop.
using Microsoft.Win32.SafeHandles;

namespace GamingCenter.Core.Windows.Interop;

internal static class HidInteropNative
{
    public static Guid HidGuid => new Guid(HidNative.HidGuidString);

    public static SafeFileHandle OpenHandle(string devicePath, DesiredAccess desiredAccess, FileShare share)
    {
        return new SafeFileHandle(NativeMethods.CreateFile(
            devicePath,
            desiredAccess,
            share,
            nint.Zero,
            FileMode.Open,
            0,
            nint.Zero), ownsHandle: true);
    }

    public static bool GetAttributes(string path, out HidNative.HIDD_ATTRIBUTES attributes)
    {
        using var handle = OpenHandle(path, DesiredAccess.ReadAttributes, FileShare.ReadWrite);
        if (handle.IsInvalid)
        {
            attributes = default;
            return false;
        }

        attributes = new HidNative.HIDD_ATTRIBUTES
        {
            Size = (uint)Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>()
        };

        return HidNative.HidD_GetAttributes(handle, ref attributes);
    }

    internal enum DesiredAccess : uint
    {
        ReadAttributes = 0x00000001,
        ReadData = 0x00000001,
        WriteData = 0x00000002,
        Read = 0x00000001,
        Write = 0x00000002
    }

    public static bool TryOpenKeyboardVidPid(
        ushort vendorId,
        ushort productId,
        out string? devicePath)
    {
        devicePath = null;
        var hidGuid = HidGuid;
        var deviceInfoSet = HidNative.SetupDiGetClassDevs(
            ref hidGuid,
            nint.Zero,
            nint.Zero,
            HidNative.DIGCF.DEVICEINTERFACE | HidNative.DIGCF.PRESENT);

        if (deviceInfoSet.IsInvalid)
            return false;

        try
        {
            var ifaceData = new HidNative.SP_DEVICE_INTERFACE_DATA
            { cbSize = (uint)Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>() };
            var index = 0u;
            while (HidNative.SetupDiEnumDeviceInterfaces(
                deviceInfoSet,
                nint.Zero,
                ref hidGuid,
                index,
                ref ifaceData))
            {
                index++;
                uint requiredSize = 0;
                var detail = new HidNative.SP_DEVICE_INTERFACE_DETAIL_DATA();
                var devInfoData = new HidNative.SP_DEVINFO_DATA
                { cbSize = (uint)Marshal.SizeOf<HidNative.SP_DEVINFO_DATA>() };

                bool ret = HidNative.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref ifaceData,
                    nint.Zero,
                    0,
                    out requiredSize,
                    ref devInfoData);

                var detailBuffer = stackalloc byte[(int)requiredSize];
                var detailSpan = new Span<byte>(detailBuffer.ToArray(), 0, (int)requiredSize);
                var structRef = MemoryMarshal.Cast<byte, HidNative.SP_DEVICE_INTERFACE_DETAIL_DATA>(detailSpan);
                structRef[0].cbSize = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? (uint)(IntPtr.Size == 8 ? 8 : 5)
                    : 0;

                var handleSpan = MemoryMarshal.Cast<byte, nint>(detailSpan);
                nint detailPtr = handleSpan[0];

                if (HidNative.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref ifaceData,
                    detailPtr,
                    requiredSize,
                    out _,
                    ref devInfoData))
                {
                    string path = new string((char*)detailPtr);
                    try
                    {
                        using var handle = OpenHandle(path, DesiredAccess.ReadAttributes, FileShare.ReadWrite);
                        if (handle.IsInvalid) continue;

                        var attrs = new HidNative.HIDD_ATTRIBUTES
                        { Size = (uint)Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
                        if (HidNative.HidD_GetAttributes(handle, ref attrs)
                            && attrs.VendorId == vendorId
                            && attrs.ProductId == productId)
                        {
                            devicePath = path;
                            return true;
                        }
                    }
                    catch
                    {
                        // ignore enumeration failure and keep searching
                    }
                }
            }
        }
        finally
        {
            deviceInfoSet.Close();
        }

        return false;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            DesiredAccess dwDesiredAccess,
            FileShare dwShareMode,
            nint lpSecurityAttributes,
            FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes,
            nint hTemplateFile);
    }
}
