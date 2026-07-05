using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingCenter.Server;

/// <summary>
/// Inline loopback enforcement for the MVP local API.
///
/// Design notes:
///  - Blocks non-loopback clients at the middleware layer before controller code runs.
///  - Optionally enforces HTTPS when the host scheme is `https`.
/// </summary>
public static class LoopbackSecurityExtensions
{
    /// <summary>
    /// Adds 403 enforcement that rejects anything not coming from localhost/link-local.
    /// </summary>
    public static IApplicationBuilder UseLoopbackOnly(this IApplicationBuilder app, bool requireHttps = false)
    {
        return app.Use(async (context, next) =>
        {
            if (!IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: non-loopback connections are not allowed.");
                return;
            }

            if (requireHttps && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Bad Request: HTTPS is required.");
                return;
            }

            await next(context);
        });
    }

    private static bool IsLoopback(System.Net.IPAddress? ip) =>
        ip is not null && (
            System.Net.IPAddress.IsLoopback(ip) ||
            ip.Equals(System.Net.IPAddress.Parse("127.0.0.1")) ||
            ip.Equals(System.Net.IPAddress.Parse("::1")) ||
            ip.IsIPv6LinkLocal);
}

