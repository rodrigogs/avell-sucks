using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GamingCenter.UI;

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
            try { GamingCenter.UI.Hardware.SensorDump.Write(dumpPath); Log($"sensor dump written: {dumpPath}"); }
            catch (Exception ex) { Log($"sensor dump failed: {ex}"); }
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
}
