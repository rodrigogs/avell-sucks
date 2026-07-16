using AvellSucks.Core.Service;
using AvellSucks.Server.Hosting;
using Xunit;

namespace AvellSucks.Server.Tests;

public class ServiceConfigBindingTests
{
    [Fact]
    public void BuildListenUrl_uses_scheme_address_and_port()
    {
        var cfg = new NetworkServiceConfig { Scheme = "http", BindAddress = "127.0.0.1", Port = 5055 };
        Assert.Equal("http://127.0.0.1:5055", ServerHostBuilder.BuildListenUrl(cfg));
    }

    [Fact]
    public void BuildListenUrl_supports_https_and_remote_address()
    {
        var cfg = new NetworkServiceConfig { Scheme = "https", BindAddress = "100.72.1.5", Port = 5060 };
        Assert.Equal("https://100.72.1.5:5060", ServerHostBuilder.BuildListenUrl(cfg));
    }
}
