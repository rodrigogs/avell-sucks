using System.Net;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class RemoteWriteGateTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("100.72.1.5", false)]
    [InlineData("192.168.1.20", false)]
    public void IsLoopback_recognizes_loopback_addresses(string ip, bool expected)
        => Assert.Equal(expected, CallerInfo.IsLoopback(IPAddress.Parse(ip)));

    [Fact]
    public void IsLoopback_false_for_null() => Assert.False(CallerInfo.IsLoopback(null));

    [Fact]
    public void Loopback_write_allowed_regardless_of_remote_flag()
    {
        Assert.True(RemoteWriteGate.Evaluate(callerIsLoopback: true, allowRemoteWrites: false).Allowed);
        Assert.True(RemoteWriteGate.Evaluate(callerIsLoopback: true, allowRemoteWrites: true).Allowed);
    }

    [Fact]
    public void Remote_write_blocked_unless_remote_writes_enabled()
    {
        var blocked = RemoteWriteGate.Evaluate(callerIsLoopback: false, allowRemoteWrites: false);
        Assert.False(blocked.Allowed);
        Assert.Contains("remote", blocked.Reason!, System.StringComparison.OrdinalIgnoreCase);

        Assert.True(RemoteWriteGate.Evaluate(callerIsLoopback: false, allowRemoteWrites: true).Allowed);
    }

    [Fact]
    public void Describe_includes_origin_and_scheme()
    {
        var s = CallerInfo.Describe(isLoopback: false, remoteIp: "100.72.1.5", authScheme: "Bearer");
        Assert.Contains("100.72.1.5", s);
        Assert.Contains("Bearer", s);
        Assert.Contains("remote", s, System.StringComparison.OrdinalIgnoreCase);
    }
}
