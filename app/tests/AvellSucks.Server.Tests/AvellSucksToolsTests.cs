using System.Net;
using AvellSucks.Api.Security;
using AvellSucks.Core.Platforms;
using AvellSucks.Core.Service;
using AvellSucks.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvellSucks.Server.Tests;

public class AvellSucksToolsTests
{
    private static AvellSucksTools Make(string remoteIp, bool allowRemote, out StubEcBackend backend)
    {
        backend = new StubEcBackend();
        backend.SetMode(new AvellSucks.Core.Models.FanMode("t", System.DateTimeOffset.UtcNow, 0, true, "Normal"));
        var writer = new SafeEcWriter(new WriteGate(true), new EcWriteAllowlist(), backend, backend, new ListAuditLog());
        var httpCtx = new DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        var authorizer = new RemoteWriteAuthorizer(
            new HttpContextAccessor { HttpContext = httpCtx },
            new StaticOptionsMonitor<NetworkServiceConfig>(new NetworkServiceConfig { AllowRemoteWrites = allowRemote }));
        return new AvellSucksTools(backend, writer, authorizer);
    }

    [Fact]
    public async Task get_fan_mode_returns_current_mode()
    {
        var tools = Make("127.0.0.1", true, out _);
        var mode = await tools.GetFanModeAsync(CancellationToken.None);
        Assert.Contains("Normal", mode);
    }

    [Fact]
    public async Task set_fan_mode_blocked_for_remote_when_remote_writes_off()
    {
        var tools = Make("100.72.1.5", allowRemote: false, out var backend);
        var result = await tools.SetFanModeAsync("boost", CancellationToken.None);
        Assert.Contains("remote", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task set_fan_mode_writes_when_allowed()
    {
        var tools = Make("100.72.1.5", allowRemote: true, out var backend);
        var result = await tools.SetFanModeAsync("boost", CancellationToken.None);
        Assert.Contains(backend.Writes, w => w.Address == 1873 && w.Value == 64);
    }
}
