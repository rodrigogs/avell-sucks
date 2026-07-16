using System.Text.Json;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class NetworkServiceConfigTests
{
    [Fact]
    public void Defaults_are_loopback_http_no_auth_no_remote_writes()
    {
        var cfg = new NetworkServiceConfig();

        Assert.Equal("127.0.0.1", cfg.BindAddress);
        Assert.Equal(5055, cfg.Port);
        Assert.Equal("http", cfg.Scheme);
        Assert.Null(cfg.HttpsCertPath);
        Assert.Null(cfg.Auth.BearerTokenSha256);
        Assert.False(cfg.Auth.MtlsEnabled);
        Assert.False(cfg.AllowRemoteWrites);
        Assert.False(cfg.McpEnabled);
        Assert.False(cfg.FirewallAutoOpen);
    }

    [Fact]
    public void Round_trips_through_camelCase_json()
    {
        var cfg = new NetworkServiceConfig
        {
            BindAddress = "100.72.1.5",
            Port = 5060,
            Scheme = "https",
            HttpsCertPath = @"C:\ProgramData\AvellSucks\cert.pfx",
            Auth = new AuthConfig { BearerTokenSha256 = "abc123", MtlsEnabled = true, MtlsCaThumbprint = "DE:AD" },
            AllowRemoteWrites = true,
            McpEnabled = true,
            FirewallAutoOpen = true,
        };

        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"bindAddress\":\"100.72.1.5\"", json);
        Assert.Contains("\"allowRemoteWrites\":true", json);

        var back = JsonSerializer.Deserialize<NetworkServiceConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("100.72.1.5", back.BindAddress);
        Assert.True(back.Auth.MtlsEnabled);
        Assert.Equal("DE:AD", back.Auth.MtlsCaThumbprint);
        Assert.True(back.McpEnabled);
    }
}
