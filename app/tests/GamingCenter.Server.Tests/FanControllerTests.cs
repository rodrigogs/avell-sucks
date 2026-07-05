using GamingCenter.Api.Controllers;
using GamingCenter.Core.Models;
using GamingCenter.Core.Platforms;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GamingCenter.Server.Tests;

public class FanControllerTests
{
    private const int FanAddr = 1873;
    private static readonly int[] CurveAddresses = [1859, 1860, 1861, 1862, 1863];

    private static (FanController controller, StubEcBackend backend, ListAuditLog audit) MakeController(
        bool allowWrites = false)
    {
        var backend = new StubEcBackend();
        var audit = new ListAuditLog();
        var writer = new SafeEcWriter(
            new WriteGate(allowWrites),
            new EcWriteAllowlist(),
            reader: backend,
            writer: backend,
            audit: audit);

        return (new FanController(backend, writer), backend, audit);
    }

    // --- GET /api/fan/mode ---

    [Fact]
    public async Task GetMode_returns_200_with_fan_mode_when_available()
    {
        var (controller, backend, _) = MakeController();
        backend.SetMode(new FanMode("test", System.DateTimeOffset.UtcNow, 160, false, "Advanced Custom"));

        var result = await controller.GetModeAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var mode = Assert.IsType<FanMode>(ok.Value);
        Assert.Equal(160, mode.RawValue);
        Assert.Equal("Advanced Custom", mode.Description);
    }

    [Fact]
    public async Task GetMode_returns_503_when_backend_cannot_read()
    {
        var (controller, backend, _) = MakeController();
        backend.SetMode(null);

        var result = await controller.GetModeAsync(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, obj.StatusCode);
    }

    // --- POST /api/fan/mode ---

    [Fact]
    public async Task SetMode_returns_400_for_unknown_mode()
    {
        var (controller, _, _) = MakeController(allowWrites: true);

        var result = await controller.SetModeAsync(new SetFanModeRequest("turbo"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetMode_returns_403_when_gate_closed()
    {
        var (controller, _, _) = MakeController(allowWrites: false);

        var result = await controller.SetModeAsync(new SetFanModeRequest("auto"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
        var writeResult = Assert.IsType<EcWriteResult>(status.Value);
        Assert.False(writeResult.Allowed);
        Assert.Contains("gate is closed", writeResult.Error);
    }

    [Fact]
    public async Task SetMode_returns_200_and_writes_correct_address_and_value()
    {
        var (controller, backend, audit) = MakeController(allowWrites: true);
        // Seed with a different mode so the write is a real state change
        backend.SetMode(new FanMode("test", System.DateTimeOffset.UtcNow, 160, false, "Advanced Custom"));

        var result = await controller.SetModeAsync(new SetFanModeRequest("boost"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var writeResult = Assert.IsType<EcWriteResult>(ok.Value);
        Assert.True(writeResult.Allowed);
        Assert.True(writeResult.Executed);
        Assert.True(writeResult.Verified);
        // Verify the correct register and value were targeted
        Assert.Equal(FanAddr, writeResult.Attempt.Address);
        Assert.Equal(64, writeResult.Attempt.RequestedValue);
        Assert.Single(audit.Entries);
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("boost", 64)]
    [InlineData("custom", 160)]
    [InlineData("L1", 129)]
    [InlineData("L5", 133)]
    public async Task SetMode_writes_correct_value_for_each_mode(string mode, int expectedValue)
    {
        var (controller, backend, _) = MakeController(allowWrites: true);
        backend.SetMode(new FanMode("test", System.DateTimeOffset.UtcNow, 0, true, "Normal"));

        var result = await controller.SetModeAsync(new SetFanModeRequest(mode), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var writeResult = Assert.IsType<EcWriteResult>(ok.Value);
        Assert.True(writeResult.Verified);
        // The requested value must match the expected EC byte for this mode
        Assert.Equal(expectedValue, writeResult.Attempt.RequestedValue);
        Assert.Equal(FanAddr, writeResult.Attempt.Address);
    }

    // --- GET /api/fan/curve ---

    [Fact]
    public async Task GetCurve_returns_five_temperature_to_pwm_levels_from_ec()
    {
        var (controller, backend, _) = MakeController();
        backend.SeedCurve([20, 45, 70, 95, 140]);

        var result = await controller.GetCurveAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var curve = Assert.IsType<FanCurveDto>(ok.Value);
        Assert.Equal([50, 60, 70, 80, 90], curve.Levels.Select(l => l.TemperatureC).ToArray());
        Assert.Equal([20, 45, 70, 95, 140], curve.Levels.Select(l => l.Pwm).ToArray());
        Assert.Equal(CurveAddresses, curve.Levels.Select(l => l.Address).ToArray());
    }

    [Fact]
    public async Task SetCurve_writes_all_pwm_levels_and_switches_to_custom_mode()
    {
        var (controller, backend, _) = MakeController(allowWrites: true);
        backend.SeedCurve([0, 0, 0, 0, 0]);
        var request = new SetFanCurveRequest([
            new FanCurveLevelDto(50, 24, 0),
            new FanCurveLevelDto(60, 48, 0),
            new FanCurveLevelDto(70, 72, 0),
            new FanCurveLevelDto(80, 96, 0),
            new FanCurveLevelDto(90, 120, 0),
        ]);

        var result = await controller.SetCurveAsync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var writeResult = Assert.IsType<FanCurveWriteResultDto>(ok.Value);
        Assert.True(writeResult.Verified);
        Assert.Equal(
            [(1859, 24), (1860, 48), (1861, 72), (1862, 96), (1863, 120), (FanAddr, 160)],
            backend.Writes);
    }

    [Fact]
    public async Task SetCurve_rejects_non_ascending_temperatures()
    {
        var (controller, _, _) = MakeController(allowWrites: true);
        var request = new SetFanCurveRequest([
            new FanCurveLevelDto(50, 20, 0),
            new FanCurveLevelDto(50, 40, 0),
            new FanCurveLevelDto(70, 60, 0),
            new FanCurveLevelDto(80, 80, 0),
            new FanCurveLevelDto(90, 100, 0),
        ]);

        var result = await controller.SetCurveAsync(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
