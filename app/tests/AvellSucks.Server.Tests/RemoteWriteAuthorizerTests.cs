using System.Net;
using System.Security.Claims;
using AvellSucks.Api.Security;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AvellSucks.Server.Tests;

public class RemoteWriteAuthorizerTests
{
    private static RemoteWriteAuthorizer Make(string remoteIp, bool allowRemoteWrites, string? scheme = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        if (scheme is not null) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(scheme));
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var cfg = new StaticOptionsMonitor<NetworkServiceConfig>(
            new NetworkServiceConfig { AllowRemoteWrites = allowRemoteWrites });
        return new RemoteWriteAuthorizer(accessor, cfg);
    }

    [Fact]
    public void Loopback_write_allowed_even_when_remote_writes_off()
        => Assert.True(Make("127.0.0.1", allowRemoteWrites: false).Check().Allowed);

    [Fact]
    public void Remote_write_denied_when_remote_writes_off()
    {
        var d = Make("100.72.1.5", allowRemoteWrites: false).Check();
        Assert.False(d.Allowed);
        Assert.Contains("remote", d.Reason!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remote_write_allowed_when_remote_writes_on()
        => Assert.True(Make("100.72.1.5", allowRemoteWrites: true).Check().Allowed);

    [Fact]
    public void DescribeCaller_reports_origin_and_scheme()
    {
        var s = Make("100.72.1.5", allowRemoteWrites: true, scheme: "Bearer").DescribeCaller();
        Assert.Contains("100.72.1.5", s);
        Assert.Contains("Bearer", s);
    }
}
