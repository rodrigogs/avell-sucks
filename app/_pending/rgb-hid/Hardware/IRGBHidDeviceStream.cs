using AvellSucks.Core.Windows.Hardware;

namespace AvellSucks.Core.Windows.Hardware;

public interface IRGBHidDeviceStream : IAsyncDisposable
{
    Task WriteAsync(byte[] buffer, CancellationToken ct = default);
}
