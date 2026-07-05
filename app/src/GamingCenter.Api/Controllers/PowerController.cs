using GamingCenter.Core.Hardware;
using GamingCenter.Core.Models;
using GamingCenter.Core.Platforms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace GamingCenter.Api.Controllers;

/// <summary>
/// Power limit control endpoints (PL1/PL2/Tau). Reads go through
/// <see cref="IEcBackend.ReadPowerProfileAsync"/>; writes go through
/// <see cref="SafeEcWriter"/> and the EC write allowlist. Writes require the
/// write gate to be open.
/// </summary>
/// <remarks>
/// The current implementation exposes speculative candidate registers with
/// permissive allowlist entries. Actual unit semantics and safe value ranges
/// are not yet confirmed by real-hardware diffing. Treat these endpoints as
/// experimental until validation is complete.
/// </remarks>
[ApiController]
[Route("api/power")]
public sealed class PowerController : ControllerBase
{
    private const int ADDR_PL1_SETTING_VALUE = 1919; // 0x77F
    private const int ADDR_PL2_SETTING_VALUE = 1920; // 0x780
    private const int ADDR_PL4_SETTING_VALUE = 1921; // 0x781
    private const int ADDR_MYFAN3_CPU_TAU = 1857;    // 0x741

    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;

    public PowerController(IEcBackend backend, SafeEcWriter writer)
    {
        _backend = backend;
        _writer = writer;
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
    public async Task<ActionResult<PowerWriteResultDto>> SetProfileAsync(
        [FromBody] SetPowerProfileRequest request, CancellationToken ct)
    {
        if (!request.HasAnyChange())
            return Ok(PowerWriteResultDto.Empty);

        var results = new List<EcWriteResult>();

        if (request.Pl1.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                ADDR_PL1_SETTING_VALUE, request.Pl1.Value,
                reason: "api:power/profile:pl1", ct).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, PowerWriteResultDto.From(results));
        }

        if (request.Pl2.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                ADDR_PL2_SETTING_VALUE, request.Pl2.Value,
                reason: "api:power/profile:pl2", ct).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, PowerWriteResultDto.From(results));
        }

        if (request.Pl4.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                ADDR_PL4_SETTING_VALUE, request.Pl4.Value,
                reason: "api:power/profile:pl4", ct).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, PowerWriteResultDto.From(results));
        }

        if (request.TauSeconds.HasValue)
        {
            var r = await _writer.TryWriteAsync(
                ADDR_MYFAN3_CPU_TAU, request.TauSeconds.Value,
                reason: "api:power/profile:tau", ct).ConfigureAwait(false);
            results.Add(r);

            if (!r.Allowed || !r.Verified)
                return StatusCode(StatusCodes.Status403Forbidden, PowerWriteResultDto.From(results));
        }

        return Ok(PowerWriteResultDto.From(results));
    }
}

/// <summary>
/// Request body for POST /api/power/profile.
/// </summary>
public sealed record SetPowerProfileRequest(int? Pl1, int? Pl2, int? Pl4, int? TauSeconds)
{
    public bool HasAnyChange() => Pl1 is not null || Pl2 is not null || Pl4 is not null || TauSeconds is not null;
}

public sealed record PowerWriteResultDto(bool Allowed, bool Executed, bool Verified, string? Error, IReadOnlyList<EcWriteResult> Results)
{
    public static PowerWriteResultDto Empty { get; } = new(true, false, true, null, []);

    public static PowerWriteResultDto From(IReadOnlyList<EcWriteResult> results)
    {
        var allowed = results.All(r => r.Allowed);
        var executed = results.All(r => r.Executed);
        var verified = results.All(r => r.Verified);
        var error = results.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Error))?.Error;
        return new PowerWriteResultDto(allowed, executed, verified, error, results);
    }
}
