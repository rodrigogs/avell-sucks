using AvellSucks.Api.Security;
using AvellSucks.Core.Hardware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AvellSucks.Api.Controllers;

/// <summary>
/// Model-specific machine controls: combined Wi-Fi/Bluetooth EC radio, the I2C
/// precision touchpad, integrated webcam, panel brightness, and interactive
/// display power. All mutations pass both the remote-write policy and the shared
/// hardware write gate inside IMachineControlService.
/// </summary>
[ApiController]
[Route("api/devices")]
public sealed class DevicesController(
    IMachineControlService controls,
    RemoteWriteAuthorizer remoteWrite) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MachineControlStatus>> GetStatusAsync(CancellationToken ct)
        => Ok(await controls.GetStatusAsync(ct).ConfigureAwait(false));

    [HttpPost("wireless")]
    public Task<ActionResult<MachineControlResult>> SetWirelessAsync(
        [FromBody] SetEnabledRequest request, CancellationToken ct)
        => ExecuteAsync(
            (origin, identity) => controls.SetWirelessRadiosAsync(
                request.Enabled, "api:devices/wireless", ct, origin, identity), ct);

    [HttpPost("touchpad")]
    public Task<ActionResult<MachineControlResult>> SetTouchpadAsync(
        [FromBody] SetEnabledRequest request, CancellationToken ct)
        => ExecuteAsync(
            (origin, identity) => controls.SetTouchpadEnabledAsync(
                request.Enabled, "api:devices/touchpad", ct, origin, identity), ct);

    [HttpPost("webcam")]
    public Task<ActionResult<MachineControlResult>> SetWebcamAsync(
        [FromBody] SetEnabledRequest request, CancellationToken ct)
        => ExecuteAsync(
            (origin, identity) => controls.SetWebcamEnabledAsync(
                request.Enabled, "api:devices/webcam", ct, origin, identity), ct);

    [HttpPost("brightness")]
    public Task<ActionResult<MachineControlResult>> SetBrightnessAsync(
        [FromBody] SetBrightnessRequest request, CancellationToken ct)
        => ExecuteAsync(
            (origin, identity) => controls.SetBrightnessAsync(
                request.Percent, "api:devices/brightness", ct, origin, identity), ct);

    [HttpPost("display/off")]
    public Task<ActionResult<MachineControlResult>> TurnOffDisplayAsync(CancellationToken ct)
        => ExecuteAsync(
            (origin, identity) => controls.TurnOffDisplayAsync(
                "api:devices/display/off", ct, origin, identity), ct);

    private async Task<ActionResult<MachineControlResult>> ExecuteAsync(
        Func<string, string?, ValueTask<MachineControlResult>> operation,
        CancellationToken cancellationToken)
    {
        var remote = remoteWrite.Check();
        if (!remote.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, remote.Reason);

        var result = await operation(
            remoteWrite.DescribeCaller(), User.Identity?.AuthenticationType).ConfigureAwait(false);
        return result.Outcome switch
        {
            MachineControlOutcome.Blocked => StatusCode(StatusCodes.Status403Forbidden, result),
            MachineControlOutcome.Failed => StatusCode(StatusCodes.Status400BadRequest, result),
            _ => Ok(result),
        };
    }
}

public sealed record SetEnabledRequest(bool Enabled);
public sealed record SetBrightnessRequest(int Percent);
