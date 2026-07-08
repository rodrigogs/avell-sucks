using Microsoft.AspNetCore.Mvc;

namespace AvellSucks.Api.Controllers;

/// <summary>
/// Root discovery endpoints for the local API.
/// </summary>
[ApiController]
public sealed class HomeController : ControllerBase
{
    /// <summary>
    /// Health/branding root.
    /// </summary>
    [HttpGet("/")]
    [Produces("text/plain")]
    public IActionResult Get() => Content("AvellSucks local API\n", "text/plain; charset=utf-8");
}
