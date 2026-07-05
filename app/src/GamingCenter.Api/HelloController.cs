using GamingCenter.Api.Models;
using GamingCenter.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamingCenter.Api.Controllers;

/// <summary>
/// Human/script-facing automation surface.
/// </summary>
[ApiController]
public sealed class HelloController : ControllerBase
{
    private readonly IHelloService _hello;

    public HelloController(IHelloService hello)
    {
        _hello = hello;
    }

    /// <summary>
    /// The local automation API now has a real example endpoint.
    /// This will eventually drive higher-level console outputs as well.
    /// </summary>
    /// <remarks>
    /// Sample:
    ///   curl -X POST http://127.0.0.1:5055/api/hello \
    ///        -H: content-type: application/json \
    ///        -d '{"name":"MVP"}'
    /// </remarks>
    /// <response code="200">Returns hello greeting.</response>
    [HttpPost("api/hello")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HelloResponse), 200)]
    public ActionResult<HelloResponse> Post([FromBody] HelloRequest request)
    {
        var response = _hello.Respond(request?.Name);
        return Ok(response);
    }
}
