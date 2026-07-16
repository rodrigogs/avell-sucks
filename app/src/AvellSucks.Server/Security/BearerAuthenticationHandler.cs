using System.Security.Claims;
using System.Text.Encodings.Web;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvellSucks.Server.Security;

/// <summary>
/// Validates "Authorization: Bearer &lt;token&gt;" against the SHA-256 hash in
/// <see cref="NetworkServiceConfig"/> using a constant-time compare. Returns
/// NoResult (never Fail) when the token is missing/wrong/unconfigured, so the
/// exposure policy — not this handler — decides whether the request is allowed
/// (loopback is exempt; a wrong token on a remote request fails authorization).
/// </summary>
public sealed class BearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Bearer";

    private readonly IOptionsMonitor<NetworkServiceConfig> _config;

    public BearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<NetworkServiceConfig> config)
        : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = _config.CurrentValue.Auth.BearerTokenSha256;
        if (string.IsNullOrEmpty(expected))
            return Task.FromResult(AuthenticateResult.NoResult());

        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var presented = header["Bearer ".Length..].Trim();
        var presentedHash = TokenHasher.HashHex(presented);
        if (!TokenHasher.FixedTimeEqualsHex(presentedHash, expected))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(Scheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, "bearer-client"));
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
