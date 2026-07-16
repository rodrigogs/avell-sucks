using System.Net;
using System.Net.Http;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Boots the real host in-process via WebApplicationFactory. TestServer does not
/// populate Connection.RemoteIpAddress, so the loopback-exempt path (which is
/// fail-closed on a null/unknown IP by design) would deny. This factory injects a
/// startup filter that stamps the client as the loopback address it genuinely is —
/// test infrastructure only; the production IsLoopback check is unchanged.
/// </summary>
public sealed class LoopbackWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, LoopbackStartupFilter>());
    }

    private sealed class LoopbackStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) =>
            {
                ctx.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return n(ctx);
            });
            next(app);
        };
    }
}

public class ExposureIntegrationTests : IClassFixture<LoopbackWebApplicationFactory>
{
    private readonly LoopbackWebApplicationFactory _factory;
    public ExposureIntegrationTests(LoopbackWebApplicationFactory factory) => _factory = factory;

    // The client presents as loopback, so unauthenticated GETs succeed
    // (proves loopback is exempt and the pipeline is wired).
    [Fact]
    public async Task Loopback_can_read_root_without_auth()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task System_snapshot_readable_on_loopback()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/system/snapshot");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
