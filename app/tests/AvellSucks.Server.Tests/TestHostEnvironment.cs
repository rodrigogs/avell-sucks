using System;
using System.Runtime.CompilerServices;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Test-host environment shim. Runs once at assembly load (before any test or
/// host construction). Forces the polling file watcher so that building a real
/// <c>WebApplication</c> in-process is stable when the source tree lives on a
/// WSL 9p mount driven by the Windows dotnet.exe: the default inotify-backed
/// watcher there hands back an already-signaled change token, and
/// <c>ChangeToken.OnChange</c> re-registers synchronously in an infinite loop
/// (StackOverflow) the moment <c>WebApplication.CreateBuilder</c> wires up
/// reloadOnChange for appsettings.json. The polling token has
/// <c>ActiveChangeCallbacks=false</c>, so no synchronous re-registration occurs.
/// Pure test-infrastructure — it changes nothing about product behavior.
///
/// Also points the service-config path (<c>GAMINGCENTER_CONFIG_PATH</c>) at a
/// temp file that is deliberately NOT created: the whole Server.Tests assembly
/// then loads config from that missing temp path (=> safe defaults => loopback
/// http), so tests that boot the real host (ExposureIntegrationTests,
/// ServerHostBuilderTests) never read the machine-global
/// %ProgramData%\AvellSucks\service.json and are immune to whatever it contains.
/// </summary>
internal static class TestHostEnvironment
{
    [ModuleInitializer]
    public static void Init()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
        Environment.SetEnvironmentVariable(
            "GAMINGCENTER_CONFIG_PATH",
            Path.Combine(Path.GetTempPath(), "avs-test-service.json"));
    }
}
