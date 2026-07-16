using System;
using System.IO;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class ServiceConfigPathsTests
{
    private const string EnvVar = "GAMINGCENTER_CONFIG_PATH";

    [Fact]
    public void ConfigFile_and_Dir_honor_the_env_override()
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            var overridePath = Path.Combine(Path.GetTempPath(), "avs-paths-test", "custom.json");
            Environment.SetEnvironmentVariable(EnvVar, overridePath);

            Assert.Equal(overridePath, ServiceConfigPaths.ConfigFile);
            Assert.Equal(Path.GetDirectoryName(overridePath), ServiceConfigPaths.Dir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }

    [Fact]
    public void ConfigFile_defaults_under_AvellSucks_when_override_cleared()
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, null);

            Assert.EndsWith(Path.Combine("AvellSucks", "service.json"), ServiceConfigPaths.ConfigFile);
            Assert.EndsWith("AvellSucks", ServiceConfigPaths.Dir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }
}
