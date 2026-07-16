using AvellSucks.Api.Security;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;
using AvellSucks.Core.Platforms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace AvellSucks.Api.Controllers;

/// <summary>
/// Power limit control endpoints (PL1/PL2/PL4). Reads go through
/// <see cref="IEcBackend.ReadPowerProfileAsync"/>; writes go through
/// <see cref="SafeEcWriter"/> and the EC write allowlist. Writes require the
/// write gate to be open.
/// </summary>
/// <remarks>
/// Targets the confirmed OEM power-limit registers 0x783/0x784/0x785 (1923/1924/
/// 1925), the same ones the read side and the EC write allowlist use, so a write
/// actually lands. Tau (time window) is an Intel XTU/MSR setting, not an EC
/// register (the EC reports it as 0), so it is not writable here.
/// </remarks>
[ApiController]
[Route("api/power")]
public sealed class PowerController : ControllerBase
{
    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;
    private readonly RemoteWriteAuthorizer _remoteWrite;

    public PowerController(IEcBackend backend, SafeEcWriter writer, RemoteWriteAuthorizer remoteWrite)
    {
        _backend = backend;
        _writer = writer;
        _remoteWrite = remoteWrite;
    }

    /// <summary>
    /// Returns the current power-limit snapshot from the EC, if supported.
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<PowerProfileState>> GetProfileAsync(CancellationToken ct)
    {
        var state = await _backend.ReadPowerProfileAsync(ct).ConfigureAwait(false);
        if (state is null)
            return Problem(
                title: "Power profile is not supported on this hardware surface.",
                statusCode: StatusCodes.Status501NotImplemented);

        if (!state.Supported || !string.IsNullOrEmpty(state.Error))
            return Problem(
                title: state.Error ?? "Power profile read returned an unsupported state.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        return Ok(state);
    }

    /// <summary>
    /// Writes targeted power-limit fields. Only the fields present in the
    /// request body are updated; omitted fields remain unchanged.
    /// </summary>
    [HttpPost("profile")]
    public async Task<ActionResult<BatchWriteResultDto>> SetProfileAsync(
        [FromBody] SetPowerProfileRequest request, CancellationToken ct)
    {
        if (!request.HasAnyChange())
            return Ok(BatchWriteResultDto.Empty);

        var gate = _remoteWrite.Check();
        if (!gate.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, gate.Reason);

        var origin = _remoteWrite.DescribeCaller();
        var identity = User.Identity?.AuthenticationType;
        var results = new List<EcWriteResult>();

        if (request.Pl1.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                PowerRegisters.Pl1, request.Pl1.Value,
                reason: "api:power/profile:pl1", cancellationToken: ct,
                origin: origin, identity: identity).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, BatchWriteResultDto.From(results));
        }

        if (request.Pl2.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                PowerRegisters.Pl2, request.Pl2.Value,
                reason: "api:power/profile:pl2", cancellationToken: ct,
                origin: origin, identity: identity).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, BatchWriteResultDto.From(results));
        }

        if (request.Pl4.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                PowerRegisters.Pl4, request.Pl4.Value,
                reason: "api:power/profile:pl4", cancellationToken: ct,
                origin: origin, identity: identity).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, BatchWriteResultDto.From(results));
        }

        return Ok(BatchWriteResultDto.From(results));
    }
}

/// <summary>
/// Request body for POST /api/power/profile.
/// </summary>
public sealed record SetPowerProfileRequest(int? Pl1, int? Pl2, int? Pl4)
{
    public bool HasAnyChange() => Pl1 is not null || Pl2 is not null || Pl4 is not null;
}
