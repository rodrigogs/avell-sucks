using GamingCenter.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace GamingCenter.Core.Windows.Hardware;

public interface IHidDeviceProvider
{
    Task<string?> OpenKeyboardAsync(CancellationToken cancellationToken = default);
    Task<byte[]?> GetFeatureAsync(string devicePath, byte reportId, CancellationToken cancellationToken = default);
    Task SetFeatureAsync(string devicePath, byte reportId, byte[] payload, CancellationToken cancellationToken = default);
    Task<IRGBHidDeviceStream?> OpenStreamAsync(string devicePath, CancellationToken cancellationToken = default);
}

public interface ITEHidDeviceProvider : IHidDeviceProvider
{
    Task<bool> TryOpenKeyboard(out string? devicePath, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetAvailableUsagePages(CancellationToken cancellationToken = default);
}

