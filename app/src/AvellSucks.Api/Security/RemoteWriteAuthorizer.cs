using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AvellSucks.Api.Security;

/// <summary>
/// Applies the SECOND write gate (remote-write) to the current request and
/// produces the audit origin/identity string. Shared by the REST controllers
/// and the MCP tools so both enforce the same rule. The first gate
/// (<see cref="Platforms.WriteGate"/>) still runs inside SafeEcWriter.
/// </summary>
public sealed class RemoteWriteAuthorizer(
    IHttpContextAccessor accessor,
    IOptionsMonitor<NetworkServiceConfig> config)
{
    private bool IsLoopback =>
        CallerInfo.IsLoopback(accessor.HttpContext?.Connection.RemoteIpAddress);

    /// <summary>Allowed for loopback always; for remote only when AllowRemoteWrites is on.</summary>
    public RemoteWriteDecision Check()
        => RemoteWriteGate.Evaluate(IsLoopback, config.CurrentValue.AllowRemoteWrites);

    /// <summary>Origin + identity for the write audit, e.g. "remote 100.72.1.5 via Bearer".</summary>
    public string DescribeCaller()
    {
        var ctx = accessor.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress;
        var scheme = ctx?.User.Identity?.IsAuthenticated == true
            ? ctx.User.Identity!.AuthenticationType
            : null;
        return CallerInfo.Describe(CallerInfo.IsLoopback(ip), ip?.ToString(), scheme);
    }
}
