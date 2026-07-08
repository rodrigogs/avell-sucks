using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace AvellSucks.UI.Hardware;

/// <summary>
/// Measures live network throughput (up/down) the way the OEM dashboard does —
/// as a byte-count delta per second over the active interface. Pure .NET, no
/// driver dependency. Call <see cref="Sample"/> once per second.
/// </summary>
public sealed class NetworkMeter
{
    private long _lastRx;
    private long _lastTx;
    private DateTime _lastAt;
    private bool _primed;

    public double DownBytesPerSec { get; private set; }
    public double UpBytesPerSec { get; private set; }

    public void Sample()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch
        {
            return; // leave last values in place on a transient failure
        }

        var now = DateTime.UtcNow;
        if (_primed)
        {
            double secs = Math.Max(0.001, (now - _lastAt).TotalSeconds);
            DownBytesPerSec = Math.Max(0, (rx - _lastRx) / secs);
            UpBytesPerSec = Math.Max(0, (tx - _lastTx) / secs);
        }
        _lastRx = rx;
        _lastTx = tx;
        _lastAt = now;
        _primed = true;
    }

    /// <summary>Human-readable rate, e.g. "6.2 Mbps" / "820 Kbps".</summary>
    public static string FormatBitsPerSec(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        if (bits >= 1_000_000) return $"{bits / 1_000_000:0.0} Mbps";
        if (bits >= 1_000) return $"{bits / 1_000:0} Kbps";
        return $"{bits:0} bps";
    }
}
