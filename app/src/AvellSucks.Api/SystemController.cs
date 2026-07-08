using AvellSucks.Api;
using AvellSucks.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AvellSucks.Api.Controllers;

/// <summary>
/// System endpoints.
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    /// <summary>
    /// Returns a point-in-time system snapshot.
    /// </summary>
    [HttpGet("snapshot")]
    public ActionResult<SystemInfo> GetSnapShot(CancellationToken ct)
        => Ok(SystemSnapshotBuilder.Build());
}
