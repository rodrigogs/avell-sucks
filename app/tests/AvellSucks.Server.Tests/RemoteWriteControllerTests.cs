using System.Net;
using AvellSucks.Api.Controllers;
using AvellSucks.Api.Security;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AvellSucks.Server.Tests;

public class RemoteWriteControllerTests
{
    private static (FanController c, StubEcBackend backend) MakeFan(string remoteIp, bool allowRemote)
    {
        var backend = new StubEcBackend();
        backend.SetMode(new AvellSucks.Core.Models.FanMode("t", System.DateTimeOffset.UtcNow, 0, true, "Normal"));
        var writer = new AvellSucks.Core.Platforms.SafeEcWriter(
            new AvellSucks.Core.Platforms.WriteGate(true),
            new AvellSucks.Core.Platforms.EcWriteAllowlist(),
            backend, backend, new ListAuditLog());

        var httpCtx = new DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        var authorizer = new RemoteWriteAuthorizer(
            new HttpContextAccessor { HttpContext = httpCtx },
            new StaticOptionsMonitor<NetworkServiceConfig>(new NetworkServiceConfig { AllowRemoteWrites = allowRemote }));

        var controller = new FanController(backend, writer, authorizer)
        {
            ControllerContext = new ControllerContext { HttpContext = httpCtx },
        };
        return (controller, backend);
    }

    [Fact]
    public async Task Remote_set_mode_denied_when_remote_writes_off()
    {
        var (controller, backend) = MakeFan("100.72.1.5", allowRemote: false);
        var result = await controller.SetModeAsync(new SetFanModeRequest("boost"), CancellationToken.None);
        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        Assert.Empty(backend.Writes); // never reached the EC
    }

    [Fact]
    public async Task Remote_set_mode_allowed_when_remote_writes_on()
    {
        var (controller, backend) = MakeFan("100.72.1.5", allowRemote: true);
        var result = await controller.SetModeAsync(new SetFanModeRequest("boost"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Contains(backend.Writes, w => w.Address == 1873 && w.Value == 64);
    }

    [Fact]
    public async Task Loopback_set_mode_allowed_when_remote_writes_off()
    {
        var (controller, backend) = MakeFan("127.0.0.1", allowRemote: false);
        var result = await controller.SetModeAsync(new SetFanModeRequest("boost"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}
