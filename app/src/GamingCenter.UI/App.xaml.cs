using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GamingCenter.UI;

public partial class App : Application
{
    // Startup diagnostics land here so they can be read from the WSL side via /mnt/c.
    // Falls back to the temp dir if the primary location is not writable.
    private static readonly string LogPath = ResolveLogPath();

    private static string ResolveLogPath()
    {
        const string primary = @"C:\Users\rdp\hermes-elevated\ui-app-startup.log";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
            return primary;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "gamingcenter-ui-startup.log");
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging crash startup.
        }
    }

    public App()
    {
        // Reset the log at the earliest possible moment and wire up global handlers.
        try { File.WriteAllText(LogPath, $"[{DateTime.Now:O}] === App ctor === session={Environment.UserInteractive}{Environment.NewLine}"); }
        catch { /* ignore */ }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup begin");
        base.OnStartup(e);

        try
        {
            var main = new MainWindow();
            Log($"MainWindow constructed; showing (state={main.WindowState}, top={main.Top}, left={main.Left})");
            main.Show();
            main.Activate();
            Log($"MainWindow shown; handle acquired, IsVisible={main.IsVisible}, ActualWidth={main.ActualWidth}");
        }
        catch (Exception ex)
        {
            Log($"FATAL in OnStartup: {ex}");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"DispatcherUnhandledException: {e.Exception}");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"AppDomain.UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }
}
