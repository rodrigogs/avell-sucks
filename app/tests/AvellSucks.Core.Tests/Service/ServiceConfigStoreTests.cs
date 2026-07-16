using System.IO;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class ServiceConfigStoreTests
{
    [Fact]
    public void Load_missing_file_returns_safe_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var cfg = ServiceConfigStore.Load(path);
        Assert.Equal("127.0.0.1", cfg.BindAddress);
        Assert.False(cfg.AllowRemoteWrites);
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults_not_throw()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "{ this is not json ");
        try
        {
            var cfg = ServiceConfigStore.Load(path);
            Assert.Equal(5055, cfg.Port);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            ServiceConfigStore.Save(path, new NetworkServiceConfig
            {
                BindAddress = "100.72.1.5", McpEnabled = true, AllowRemoteWrites = true,
            });
            var back = ServiceConfigStore.Load(path);
            Assert.Equal("100.72.1.5", back.BindAddress);
            Assert.True(back.McpEnabled);
            Assert.True(back.AllowRemoteWrites);
        }
        finally { File.Delete(path); }
    }
}
