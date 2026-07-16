using System.Net;

namespace AvellSucks.Core.Service;

/// <summary>
/// Classifies a request's origin. Single source of truth for what counts as
/// "loopback" (shared by the exposure middleware, the authorization policy, and
/// the remote-write gate) and for the audit origin/identity string.
/// </summary>
public static class CallerInfo
{
    /// <summary>
    /// True only for MACHINE-LOCAL loopback: 127.0.0.0/8 and ::1 (both covered by
    /// IPAddress.IsLoopback). IPv6 link-local (fe80::/10) is deliberately NOT
    /// loopback — it reaches a same-physical-link neighbor, not this machine, and
    /// this predicate now gates authentication itself (not just a loopback-only
    /// bind), so a link-local neighbor must authenticate. Null (unknown) is treated
    /// as NOT loopback — fail closed.
    /// </summary>
    public static bool IsLoopback(IPAddress? ip)
        => ip is not null && IPAddress.IsLoopback(ip);

    /// <summary>
    /// Human-readable origin+identity for the write audit, e.g.
    /// "remote 100.72.1.5 via Bearer" or "loopback".
    /// </summary>
    public static string Describe(bool isLoopback, string? remoteIp, string? authScheme)
    {
        if (isLoopback) return "loopback";
        var who = string.IsNullOrEmpty(authScheme) ? "unauthenticated" : $"via {authScheme}";
        return $"remote {remoteIp ?? "unknown"} {who}";
    }
}
