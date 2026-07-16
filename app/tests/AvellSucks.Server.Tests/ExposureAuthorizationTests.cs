using System.Net;
using System.Security.Claims;
using AvellSucks.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AvellSucks.Server.Tests;

public class ExposureAuthorizationTests
{
    private static async Task<bool> EvaluateAsync(string remoteIp, bool authenticated)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var handler = new ExposureAuthorizationHandler(accessor);

        var user = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity("Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        var req = new LoopbackOrAuthenticatedRequirement();
        var authCtx = new AuthorizationHandlerContext([req], user, null);

        await handler.HandleAsync(authCtx);
        return authCtx.HasSucceeded;
    }

    [Fact]
    public async Task Loopback_without_auth_is_allowed()
        => Assert.True(await EvaluateAsync("127.0.0.1", authenticated: false));

    [Fact]
    public async Task Remote_without_auth_is_denied()
        => Assert.False(await EvaluateAsync("100.72.1.5", authenticated: false));

    [Fact]
    public async Task Remote_with_auth_is_allowed()
        => Assert.True(await EvaluateAsync("100.72.1.5", authenticated: true));
}
