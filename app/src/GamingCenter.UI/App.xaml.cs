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

    public App()
    {
        DispatcherUnhandledException += (_, e) => Log($"DispatcherUnhandledException: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
