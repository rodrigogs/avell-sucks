using GamingCenter.Core.Windows.Hardware;

namespace GamingCenter.Core.Windows.Hardware;

public interface IRGBHidDeviceStream : IAsyncDisposable
{
    Task WriteAsync(byte[] buffer, CancellationToken ct = default);
}
