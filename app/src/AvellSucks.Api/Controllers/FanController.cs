using AvellSucks.Core.Hardware;
using AvellSucks.Core.Models;
using AvellSucks.Core.Platforms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AvellSucks.Api.Controllers;

/// <summary>
/// Fan control endpoints. All writes go through SafeEcWriter (gate +
/// allowlist + audit). Writes are disabled unless GAMINGCENTER_ALLOW_EC_WRITES=1.
/// </summary>
[ApiController]
[Route("api/fan")]
public sealed class FanController : ControllerBase
{
    // Fan control surface — shared with the UI/reconciler/allowlist via FanModeMap.
    private const int MAFanControl = FanModeMap.ControlByteAddress; // 0x751
    private const int CustomFanMode = FanModeMap.CustomModeByte;
    private const int MaxPwm = FanModeMap.MaxPwm;
    private static readonly int[] s_curveAddresses = FanModeMap.CurveAddresses;
    private static readonly int[] s_defaultCurveTemps = FanModeMap.DefaultCurveTemps;

    private readonly IEcBackend _backend;
    private readonly SafeEcWriter _writer;

    public FanController(IEcBackend backend, SafeEcWriter writer)
    {
        _backend = backend;
        _writer = writer;
    }

    /// <summary>
    /// Returns the current interpreted fan mode.
    /// </summary>
    [HttpGet("mode")]
    public async Task<ActionResult<FanMode>> GetModeAsync(CancellationToken ct)
    {
        var mode = await _backend.InterpretFanModeAsync(ct).ConfigureAwait(false);
        if (mode is null)
            return Problem(
                title: "Could not read fan mode from EC.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        return Ok(mode);
    }

    /// <summary>
    /// Diagnostic endpoint: returns the raw EC snapshot for key fan-control
    /// addresses. Useful for debugging WMI access issues without enabling writes.
    /// </summary>
    [HttpGet("diagnostic")]
    public async Task<ActionResult<EcSnapshot>> GetDiagnosticAsync(CancellationToken ct)
    {
        var addresses = new[] { 1873, 1858, 1857, 1859, 1860, 1861, 1862, 1863 };
        var snapshot = await _backend.ReadSnapshotAsync(addresses, ct).ConfigureAwait(false);
        return Ok(snapshot);
    }

    /// <summary>
    /// Returns the five-point custom fan curve as temperature-to-PWM levels.
    /// The current EC exposes PWM source bytes only, so temperatures are the
    /// fixed UI breakpoints used by Gaming Center's L1..L5 custom levels.
    /// </summary>
    [HttpGet("curve")]
    public async Task<ActionResult<FanCurveDto>> GetCurveAsync(CancellationToken ct)
    {
        var snapshot = await _backend.ReadSnapshotAsync(s_curveAddresses, ct).ConfigureAwait(false);
        var levels = snapshot.Fields
            .Select((field, index) => new FanCurveLevelDto(
                s_defaultCurveTemps[index], field.Value, field.Address))
            .ToArray();

        return Ok(new FanCurveDto(levels));
    }

    /// <summary>
    /// Writes the five custom PWM levels and switches the fan mode to custom.
    /// Writes remain gated by GAMINGCENTER_ALLOW_EC_WRITES and the EC allowlist.
    /// </summary>
    [HttpPost("curve")]
    public async Task<ActionResult<FanCurveWriteResultDto>> SetCurveAsync(
        [FromBody] SetFanCurveRequest request,
        CancellationToken ct)
    {
        if (!TryValidateCurve(request, out var error))
            return BadRequest(error);

        var results = new List<EcWriteResult>(s_curveAddresses.Length + 1);
        for (var i = 0; i < s_curveAddresses.Length; i++)
        {
            var level = request.Levels[i];
            var result = await _writer.TryWriteAsync(
                s_curveAddresses[i], level.Pwm,
                reason: $"api:fan/curve:L{i + 1}={level.TemperatureC}C:{level.Pwm}",
                ct).ConfigureAwait(false);
            results.Add(result);

            if (!result.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, FanCurveWriteResultDto.From(results));
            if (!result.Verified)
                return StatusCode(StatusCodes.Status500InternalServerError, FanCurveWriteResultDto.From(results));
        }

        var modeResult = await _writer.TryWriteAsync(
            MAFanControl, CustomFanMode,
            reason: "api:fan/curve:enable-custom",
            ct).ConfigureAwait(false);
        results.Add(modeResult);

        if (!modeResult.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, FanCurveWriteResultDto.From(results));
        if (!modeResult.Verified)
            return StatusCode(StatusCodes.Status500InternalServerError, FanCurveWriteResultDto.From(results));

        return Ok(FanCurveWriteResultDto.From(results));
    }

    private static bool TryValidateCurve(SetFanCurveRequest? request, out string error)
    {
        if (request?.Levels is null || request.Levels.Count != s_curveAddresses.Length)
        {
            error = "Fan curve must contain exactly five levels.";
            return false;
        }

        var previousTemp = int.MinValue;
        for (var i = 0; i < request.Levels.Count; i++)
        {
            var level = request.Levels[i];
            if (level.TemperatureC < 30 || level.TemperatureC > 100)
            {
                error = $"Level {i + 1} temperature must be between 30°C and 100°C.";
                return false;
            }
            if (level.TemperatureC <= previousTemp)
            {
                error = "Fan curve temperatures must be strictly ascending.";
                return false;
            }
            if (level.Pwm < 0 || level.Pwm > MaxPwm)
            {
                error = $"Level {i + 1} PWM must be between 0 and {MaxPwm}.";
                return false;
            }
            previousTemp = level.TemperatureC;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Sets the fan mode. Only allowlisted values are accepted.
    /// </summary>
    /// <remarks>
    /// Accepted modes: auto(0), boost(64), custom(160), L1(129)..L5(133).
    /// </remarks>
    [HttpPost("mode")]
    public async Task<ActionResult<EcWriteResult>> SetModeAsync(
        [FromBody] SetFanModeRequest request,
        CancellationToken ct)
    {
        if (!FanModeMap.TryByteFor(request.Mode ?? "", out var value))
            return BadRequest(
                $"Unknown mode '{request.Mode}'. Valid: auto, boost, custom, L1..L5");

        var result = await _writer.TryWriteAsync(
            MAFanControl, value,
            reason: $"api:fan/mode={request.Mode}",
            ct).ConfigureAwait(false);

        if (!result.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        if (!result.Verified)
            return StatusCode(StatusCodes.Status500InternalServerError, result);

        return Ok(result);
    }
}

/// <summary>
/// Request body for POST /api/fan/mode.
/// </summary>
public sealed record SetFanModeRequest(string Mode);

public sealed record FanCurveLevelDto(int TemperatureC, int Pwm, int Address);

public sealed record FanCurveDto(IReadOnlyList<FanCurveLevelDto> Levels);

public sealed record SetFanCurveRequest(IReadOnlyList<FanCurveLevelDto> Levels);

public sealed record FanCurveWriteResultDto(bool Allowed, bool Executed, bool Verified, string? Error, IReadOnlyList<EcWriteResult> Results)
{
    public static FanCurveWriteResultDto From(IReadOnlyList<EcWriteResult> results)
    {
        var allowed = results.All(r => r.Allowed);
        var executed = results.All(r => r.Executed);
        var verified = results.All(r => r.Verified);
        var error = results.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Error))?.Error;
        return new FanCurveWriteResultDto(allowed, executed, verified, error, results);
    }
}

