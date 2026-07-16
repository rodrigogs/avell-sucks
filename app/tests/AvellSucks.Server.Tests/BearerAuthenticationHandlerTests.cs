using System.Text.Encodings.Web;
using AvellSucks.Core.Service;
using AvellSucks.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvellSucks.Server.Tests;

public class BearerAuthenticationHandlerTests
{
    private static async Task<AuthenticateResult> RunAsync(string? header, NetworkServiceConfig cfg)
    {
        var monitor = new StaticOptionsMonitor<NetworkServiceConfig>(cfg);
        var handler = new BearerAuthenticationHandler(
            new OptionsMonitorStub(), NullLoggerFactory.Instance, UrlEncoder.Default, monitor);

        var ctx = new DefaultHttpContext();
        if (header is not null) ctx.Request.Headers.Authorization = header;
        await handler.InitializeAsync(
            new AuthenticationScheme(BearerAuthenticationHandler.Scheme, null, typeof(BearerAuthenticationHandler)),
            ctx);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task Valid_token_authenticates()
    {
        var cfg = new NetworkServiceConfig { Auth = { BearerTokenSha256 = TokenHasher.HashHex("good-token") } };
        var result = await RunAsync("Bearer good-token", cfg);
        Assert.True(result.Succeeded);
        Assert.Equal("Bearer", result.Ticket!.AuthenticationScheme);
    }

    [Fact]
    public async Task Wrong_token_is_no_result()
    {
        var cfg = new NetworkServiceConfig { Auth = { BearerTokenSha256 = TokenHasher.HashHex("good-token") } };
        var result = await RunAsync("Bearer wrong", cfg);
        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task No_header_or_no_configured_token_is_no_result()
    {
        Assert.True((await RunAsync(null, new NetworkServiceConfig())).None);
        Assert.True((await RunAsync("Bearer x", new NetworkServiceConfig())).None); // no token configured
    }
}
