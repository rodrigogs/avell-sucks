using AvellSucks.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace AvellSucks.Core.Windows.Hardware;

internal sealed class HidDeviceStream : IRGBHidDeviceStream
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private bool _disposed;

    public HidDeviceStream(SafeFileHandle handle, CancellationToken ct = default)
    {
        _handle = handle;
        _stream = new FileStream(_handle, FileAccess.ReadWrite, 65, isAsync: true);
    }

    public async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(HidDeviceStream));
        if (buffer is null || buffer.Length == 0)
            return;

        await _stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // ignore dispose exceptions
        }

        try
        {
            _handle?.Dispose();
        }
        catch
        {
            // ignore dispose exceptions  
        }

        return ValueTask.CompletedTask;
    }
}
