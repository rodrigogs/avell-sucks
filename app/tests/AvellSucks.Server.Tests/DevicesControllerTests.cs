using System.Net;
using AvellSucks.Api.Controllers;
using AvellSucks.Api.Security;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AvellSucks.Server.Tests;

public sealed class DevicesControllerTests
{
    [Fact]
    public async Task Get_status_returns_the_machine_control_snapshot()
    {
        var (controller, _, _) = Make("127.0.0.1", allowRemote: false);

        var result = await controller.GetStatusAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var status = Assert.IsType<MachineControlStatus>(ok.Value);
        Assert.True(status.WirelessRadiosEnabled);
        Assert.Equal<byte?>(75, status.BrightnessPercent);
    }

    [Fact]
    public async Task Remote_touchpad_write_is_denied_when_remote_writes_are_off()
    {
        var (controller, _, platform) = Make("100.72.1.5", allowRemote: false);

        var result = await controller.SetTouchpadAsync(new SetEnabledRequest(false), CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(0, platform.TouchpadCalls);
    }

    [Fact]
    public async Task Loopback_touchpad_write_reaches_the_gated_machine_service()
    {
        var (controller, _, platform) = Make("127.0.0.1", allowRemote: false);

        var result = await controller.SetTouchpadAsync(new SetEnabledRequest(false), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<MachineControlResult>(ok.Value);
        Assert.Equal(MachineControlOutcome.Verified, body.Outcome);
        Assert.Equal(1, platform.TouchpadCalls);
    }

    [Fact]
    public async Task Brightness_validation_failure_is_a_bad_request()
    {
        var (controller, _, platform) = Make("127.0.0.1", allowRemote: false);

        var result = await controller.SetBrightnessAsync(new SetBrightnessRequest(101), CancellationToken.None);

        var bad = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, bad.StatusCode);
        Assert.Equal(0, platform.BrightnessCalls);
    }

    private static (DevicesController Controller, StubEcBackend Ec, ControllerPlatformBackend Platform)
        Make(string remoteIp, bool allowRemote)
    {
        var ec = new StubEcBackend();
        ec.Seed(MachineControlService.DeviceStateAddress, 0xB0);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        var platform = new ControllerPlatformBackend();
        var service = new MachineControlService(
            ec, ec, platform, new WriteGate(true), new ControllerMachineAuditLog());

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        var authorizer = new RemoteWriteAuthorizer(
            new HttpContextAccessor { HttpContext = http },
            new StaticOptionsMonitor<NetworkServiceConfig>(new NetworkServiceConfig { AllowRemoteWrites = allowRemote }));

        var controller = new DevicesController(service, authorizer)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, ec, platform);
    }

    private sealed class ControllerPlatformBackend : IPlatformMachineControlBackend
    {
        public int TouchpadCalls { get; private set; }
        public int BrightnessCalls { get; private set; }

        public ValueTask<PlatformMachineControlStatus> GetStatusAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new PlatformMachineControlStatus(
                true, true, true, true, true, 75, true, null));

        public ValueTask<PlatformMutationResult> ReconcileWirelessRadiosAsync(bool enabled, CancellationToken ct = default)
            => ValueTask.FromResult(PlatformMutationResult.Success());

        public ValueTask<PlatformMutationResult> SetTouchpadEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            TouchpadCalls++;
            return ValueTask.FromResult(PlatformMutationResult.Success());
        }

        public ValueTask<PlatformMutationResult> SetWebcamEnabledAsync(bool enabled, CancellationToken ct = default)
            => ValueTask.FromResult(PlatformMutationResult.Success());

        public ValueTask<PlatformMutationResult> SetBrightnessAsync(byte percent, CancellationToken ct = default)
        {
            BrightnessCalls++;
            return ValueTask.FromResult(PlatformMutationResult.Success());
        }

        public ValueTask<PlatformMutationResult> TurnOffDisplayAsync(CancellationToken ct = default)
            => ValueTask.FromResult(PlatformMutationResult.Accepted());
    }

    private sealed class ControllerMachineAuditLog : IMachineControlAuditLog
    {
        public ValueTask RecordAsync(MachineControlResult result, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
