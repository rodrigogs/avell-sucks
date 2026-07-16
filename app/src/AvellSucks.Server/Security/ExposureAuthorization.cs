using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace AvellSucks.Server.Security;

/// <summary>Authorization requirement: request is loopback OR authenticated.</summary>
public sealed class LoopbackOrAuthenticatedRequirement : IAuthorizationRequirement;

/// <summary>
/// The fail-closed exposure rule. Loopback callers pass without credentials
/// (preserving local automation). Non-loopback callers must be authenticated
/// (bearer and/or client cert). If no auth is configured, remote callers can't
/// authenticate, so they're denied — exposure is never accidentally open.
/// </summary>
public sealed class ExposureAuthorizationHandler(IHttpContextAccessor accessor)
    : AuthorizationHandler<LoopbackOrAuthenticatedRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, LoopbackOrAuthenticatedRequirement requirement)
    {
        var ip = accessor.HttpContext?.Connection.RemoteIpAddress;
        if (CallerInfo.IsLoopback(ip) || context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Policy name + registration constants.</summary>
public static class ExposureAuthorization
{
    public const string PolicyName = "LoopbackOrAuthenticated";
}
