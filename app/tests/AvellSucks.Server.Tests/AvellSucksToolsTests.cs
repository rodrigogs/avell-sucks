using System.Net;
using AvellSucks.Api.Security;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using AvellSucks.Core.Service;
using AvellSucks.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvellSucks.Server.Tests;

public class AvellSucksToolsTests
{
    private static AvellSucksTools Make(
        string remoteIp, bool allowRemote, out StubEcBackend backend,
        out ToolPlatformBackend platform)
    {
        backend = new StubEcBackend();
        backend.SetMode(new AvellSucks.Core.Models.FanMode("t", System.DateTimeOffset.UtcNow, 0, true, "Normal"));
        backend.Seed(MachineControlService.DeviceStateAddress, 0xB0);
        backend.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        platform = new ToolPlatformBackend();
        var writer = new SafeEcWriter(new WriteGate(true), new EcWriteAllowlist(), backend, backend, new ListAuditLog());
        var machine = new MachineControlService(
            backend, backend, platform, new WriteGate(true), new ToolMachineAuditLog());
        var httpCtx = new DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        var authorizer = new RemoteWriteAuthorizer(
            new HttpContextAccessor { HttpContext = httpCtx },
            new StaticOptionsMonitor<NetworkServiceConfig>(new NetworkServiceConfig { AllowRemoteWrites = allowRemote }));
        return new AvellSucksTools(backend, writer, machine, authorizer);
    }

    [Fact]
    public async Task get_fan_mode_returns_current_mode()
    {
        var tools = Make("127.0.0.1", true, out _, out _);
        var mode = await tools.GetFanModeAsync(CancellationToken.None);
        Assert.Contains("Normal", mode);
    }

    [Fact]
    public async Task set_fan_mode_blocked_for_remote_when_remote_writes_off()
    {
        var tools = Make("100.72.1.5", allowRemote: false, out var backend, out _);
        var result = await tools.SetFanModeAsync("boost", CancellationToken.None);
        Assert.Contains("remote", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task set_fan_mode_writes_when_allowed()
    {
        var tools = Make("100.72.1.5", allowRemote: true, out var backend, out _);
        var result = await tools.SetFanModeAsync("boost", CancellationToken.None);
        Assert.Contains(backend.Writes, w => w.Address == 1873 && w.Value == 64);
    }

    [Fact]
    public async Task get_machine_controls_returns_current_status()
    {
        var tools = Make("127.0.0.1", true, out _, out _);

        var result = await tools.GetMachineControlsAsync(CancellationToken.None);

        Assert.Contains("wirelessRadiosEnabled", result);
        Assert.Contains("brightnessPercent", result);
    }

    [Fact]
    public async Task remote_touchpad_control_obeys_remote_write_gate()
    {
        var tools = Make("100.72.1.5", allowRemote: false, out _, out var platform);

        var result = await tools.SetTouchpadEnabledAsync(false, CancellationToken.None);

        Assert.Contains("remote", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, platform.TouchpadCalls);
    }

    internal sealed class ToolPlatformBackend : IPlatformMachineControlBackend
    {
        public int TouchpadCalls { get; private set; }
        public ValueTask<PlatformMachineControlStatus> GetStatusAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new PlatformMachineControlStatus(true, true, true, true, true, 75, true, null));
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
            => ValueTask.FromResult(PlatformMutationResult.Success());
        public ValueTask<PlatformMutationResult> TurnOffDisplayAsync(CancellationToken ct = default)
            => ValueTask.FromResult(PlatformMutationResult.Accepted());
    }

    private sealed class ToolMachineAuditLog : IMachineControlAuditLog
    {
        public ValueTask RecordAsync(MachineControlResult result, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
