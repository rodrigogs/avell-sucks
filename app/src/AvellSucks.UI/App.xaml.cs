using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AvellSucks.UI;

public partial class App : Application
{
    // Crash log next to the executable so startup failures are diagnosable in the field.
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "gamingcenter-ui.log");

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}"); }
        catch { /* never let logging crash the app */ }
    }

    /// <summary>Diagnostic log hook for the rest of the app (e.g. reconciler events).</summary>
    public static void Trace(string message) => Log(message);

    public App()
    {
        DispatcherUnhandledException += (_, e) => Log($"DispatcherUnhandledException: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostic: dump every hardware sensor and exit. Used to match sensor
        // names precisely against the real machine (GC_DUMP_SENSORS=<path>).
        var dumpPath = Environment.GetEnvironmentVariable("GC_DUMP_SENSORS");
        Log($"startup: GC_DUMP_SENSORS={dumpPath ?? "(unset)"}");
        if (!string.IsNullOrWhiteSpace(dumpPath))
        {
            try { AvellSucks.UI.Hardware.SensorDump.Write(dumpPath); Log($"sensor dump written: {dumpPath}"); }
            catch (Exception ex) { Log($"sensor dump failed: {ex}"); }
            Shutdown();
            return;
        }

        // Diagnostic: exercise the real fan-service write path headlessly and exit.
        // GC_SELFTEST=fan-boost drives WmiFanService.SetModeAsync("boost") through
        // the exact pipeline the UI uses (SafeEcWriter → EC), so the outcome can be
        // verified from the audit log without any UI automation.
        var selftest = Environment.GetEnvironmentVariable("GC_SELFTEST")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(selftest))
        {
            RunSelfTest(selftest);
            Shutdown();
            return;
        }

        try
        {
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            Log($"FATAL in OnStartup: {ex}");
            throw;
        }
    }

    private static void RunSelfTest(string test)
    {
        try
        {
            var fan = AvellSucks.UI.Services.HardwareServices.CreateFanService();
            Log($"selftest '{test}': fan service = {fan.GetType().Name}, writesEnabled={fan.WritesEnabled}");
            switch (test)
            {
                case "fan-boost":
                    var r1 = fan.SetModeAsync("boost").AsTask().GetAwaiter().GetResult();
                    Log($"selftest fan-boost: State={r1.State} Verified={r1.Verified} Error={r1.Error ?? "none"}");
                    var back = fan.GetModeAsync().AsTask().GetAwaiter().GetResult();
                    Log($"selftest fan-boost: GetMode after = {back}");
                    var r0 = fan.SetModeAsync("auto").AsTask().GetAwaiter().GetResult();
                    Log($"selftest fan-boost: restore auto State={r0.State}");
                    break;
                case "fan-transitions":
                    // Reproduce the reported boost→custom failure through the real path.
                    foreach (var seq in new[] { "boost", "custom", "auto", "custom", "boost", "auto" })
                    {
                        var r = fan.SetModeAsync(seq).AsTask().GetAwaiter().GetResult();
                        Log($"selftest transition → {seq}: State={r.State} Error={r.Error ?? "none"}");
                    }
                    break;
                default:
                    Log($"selftest: unknown test '{test}'");
                    break;
            }
        }
        catch (Exception ex) { Log($"selftest '{test}' threw: {ex}"); }
    }
}
