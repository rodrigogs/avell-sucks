using Microsoft.AspNetCore.Mvc;

namespace GamingCenter.Api.Controllers;

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
    public IActionResult Get() => Content("GamingCenter local API\n", "text/plain; charset=utf-8");
}
