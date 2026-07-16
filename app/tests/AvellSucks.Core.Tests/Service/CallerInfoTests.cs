using System.Net;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class CallerInfoTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]   // IPv4 loopback
    [InlineData("127.0.0.5", true)]   // 127.0.0.0/8 loopback
    [InlineData("::1", true)]         // IPv6 loopback
    [InlineData("fe80::1", false)]    // IPv6 link-local → REMOTE, must authenticate
    [InlineData("100.72.1.5", false)] // normal remote IPv4
    [InlineData("192.168.1.20", false)]
    public void IsLoopback_is_machine_local_only(string ip, bool expected)
        => Assert.Equal(expected, CallerInfo.IsLoopback(IPAddress.Parse(ip)));

    [Fact]
    public void IsLoopback_false_for_null() => Assert.False(CallerInfo.IsLoopback(null));
}
