using AvellSucks.Server.Hosting;
using Xunit;

namespace AvellSucks.Server.Tests;

public class ServerHostBuilderTests
{
    [Theory]
    [InlineData(new string[0], 5055)]
    [InlineData(new[] { "6000" }, 6000)]
    [InlineData(new[] { "notaport" }, 5055)]
    [InlineData(new[] { "70000" }, 5055)] // out of range → default
    public void ResolvePort_parses_first_arg_or_defaults(string[] args, int expected)
        => Assert.Equal(expected, ServerHostBuilder.ResolvePort(args));

    [Fact]
    public async Task Build_produces_a_configured_web_application()
    {
        // Dispose the host when done so its config file-watch change-token
        // registrations are torn down (a dangling reloadOnChange watcher on the
        // WSL filesystem recurses and crashes the test host).
        await using var app = ServerHostBuilder.Build([]);
        Assert.NotNull(app);
        // Services from the pipeline resolve (proves DI wiring didn't regress).
        Assert.NotNull(app.Services.GetService(typeof(AvellSucks.Core.Platforms.SafeEcWriter)));
        Assert.NotNull(app.Services.GetService(typeof(AvellSucks.Core.Hardware.IMachineControlService)));
        Assert.NotNull(app.Services.GetService(typeof(AvellSucks.Core.Hardware.IPlatformMachineControlBackend)));
    }
}
