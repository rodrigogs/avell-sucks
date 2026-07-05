using GamingCenter.Core.Rgb;
using Microsoft.AspNetCore.Mvc;

namespace GamingCenter.Api.Controllers;

[ApiController]
[Route("api/rgb")]
public sealed class RgbController : ControllerBase
{
    private readonly ITERGBBackend _backend;

    public RgbController(ITERGBBackend backend)
    {
        _backend = backend;
    }

    [HttpGet("state")]
    public ActionResult<RgbState> GetState(CancellationToken ct)
    {
        var state = _backend.GetStateAsync(ct).GetAwaiter().GetResult();
        if (state is null) return NotFound("ITE RGB not supported.");
        return Ok(state);
    }

    [HttpPost("effect")]
    public ActionResult<WriteResponse> ApplyEffect([FromBody] ApplyEffectRequest request, CancellationToken ct)
    {
        if (request.Effect is null || request.Effect.Colors is null || request.Effect.Colors.Count == 0)
            return BadRequest(new WriteResponse(false, "effect.colors required"));

        var result = _backend.ApplyEffectAsync(request.Effect, ct).GetAwaiter().GetResult();
        if (!result.Ok)
            return StatusCode(500, new WriteResponse(false, result.Error));

        return Ok(new WriteResponse(true));
    }

    [HttpGet("supported")]
    public ActionResult<bool> IsSupported(CancellationToken ct)
    {
        bool ok = _backend.IsSupportedAsync(ct).GetAwaiter().GetResult();
        return Ok(ok);
    }
}
