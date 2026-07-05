using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GamingCenter.Core.Windows.Hardware;

internal readonly struct HidCapsPayload
{
    public ushort UsagePage;
    public ushort Usage;
}
