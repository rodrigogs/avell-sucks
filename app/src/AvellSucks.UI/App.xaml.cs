using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AvellSucks.UI;

public partial class App : Application
{
    // Per-machine writable log (see AppPaths): Program Files is read-only, so the
    // trace log lives under %ProgramData%\AvellSucks and survives updates.
    private static readonly string LogPath = AppPaths.LogFile;

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}"); }
        catch { /* never let logging crash the app */ }
    }

    /// <summary>Diagnostic log hook for the rest of the app (e.g. reconciler events).</summary>
    public static void Trace(string message) => Log(message);

    public App()
    {
        // Last-resort safety net: log and KEEP RUNNING rather than terminate on an
        // unhandled dispatcher exception. The write pipeline already returns failures
        // as results instead of throwing; this catches anything else that slips out
        // of an async-void handler so a stray exception can't kill the app.
        DispatcherUnhandledException += (_, e) =>
        {
            Log($"DispatcherUnhandledException (handled): {e.Exception}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply the persisted UI language before any window is built, so the
        // first paint is already in the right language (default: system culture,
        // pt/pt-BR → PT, everything else → EN).
        AvellSucks.UI.Settings.SettingsStore.Current.ApplyLanguage();

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
            var window = new MainWindow();
            if (AvellSucks.UI.Settings.SettingsStore.Current.Settings.StartMinimized)
            {
                // Start straight to the tray. Setting WindowState=Minimized before
                // Show() does NOT raise StateChanged (the HWND isn't sourced yet), so
                // relying on OnWindowStateChanged to hide would silently leave the
                // window on the taskbar. Drive it explicitly here.
                window.WindowState = WindowState.Minimized;
                if (AvellSucks.UI.Settings.SettingsStore.Current.Settings.HideOnMinimize)
                    window.StartHiddenInTray();
                else
                    window.Show();
            }
            else
            {
                window.Show();
            }

            // Silent background update check. Only NOTIFIES (a toast pointing at
            // Settings) — never auto-applies on launch, which would yank the app
            // out from under the user. Failures/no-connection are ignored silently.
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log($"FATAL in OnStartup: {ex}");
            throw;
        }
    }

    // Background update check on startup: notify via a toast if a newer release
    // exists; the user applies it from Settings. Best-effort — swallow everything.
    private static async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var check = await AvellSucks.UI.Startup.Updater.CheckAsync().ConfigureAwait(true);
            if (check.Status == AvellSucks.UI.Startup.UpdateStatus.UpdateAvailable)
            {
                Current.Dispatcher.Invoke(() => AvellSucks.UI.Controls.Toaster.Info(
                    string.Format(AvellSucks.UI.Localization.Loc.T("Update.Available.Title"), check.LatestVersion),
                    AvellSucks.UI.Localization.Loc.T("Update.Available.Hint")));
            }
        }
        catch (Exception ex) { Log($"startup update check failed (ignored): {ex.Message}"); }
    }

    private static void RunSelfTest(string test)
    {
        try
        {
            var fan = AvellSucks.UI.Services.HardwareServices.CreateFanService();
            Log($"selftest '{test}': fan service = {fan.GetType().Name}, writesEnabled={fan.WritesEnabled}");
            switch (test)
            {
                case "i18n":
                    RunI18nSelfTest();
                    break;
                case "autostart":
                    RunAutoStartSelfTest();
                    break;
                case "sensors":
                    RunSensorsSelfTest();
                    break;
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

    // Headless sensor check: opens the SensorPump exactly as the Dashboard does and
    // reports SensorsAvailable + a live sample once the off-thread monitor resolves.
    // The dashboard's "sensors unavailable" notice is now keyed on this flag, so
    // this proves whether the notice would (correctly) stay hidden.
    private static void RunSensorsSelfTest()
    {
        // Open the same ring-0 monitor the SensorPump opens (SensorsAvailable is just
        // "did HardwareMonitor construct + does it return a sample"). If this yields a
        // live sample, the dashboard's SensorsAvailable flag is true and the fixed
        // notice logic keeps the "unavailable" banner HIDDEN.
        try
        {
            var mon = new AvellSucks.UI.Hardware.HardwareMonitor();
            var t = mon.GetTelemetry();
            bool available = t is not null;
            Log($"selftest sensors: monitor opened, sample={(available ? "yes" : "null")}, " +
                $"cpuTemp={t?.CpuTempC?.ToString() ?? "n/a"} gpuTemp={t?.GpuTempC?.ToString() ?? "n/a"} " +
                $"→ dashboard notice would be {(available ? "HIDDEN (correct)" : "SHOWN")}");
            mon.Dispose();
        }
        catch (Exception ex)
        {
            Log($"selftest sensors: monitor FAILED to open ({ex.Message}) → notice SHOWN (correct)");
        }
    }

    // Headless autostart check: exercises the scheduled-task path end to end.
    private static void RunAutoStartSelfTest()
    {
        var before = AvellSucks.UI.Startup.AutoStart.IsEnabled();
        Log($"autostart: initial IsEnabled={before}");
        AvellSucks.UI.Startup.AutoStart.Set(true);
        var afterEnable = AvellSucks.UI.Startup.AutoStart.IsEnabled();
        Log($"autostart: after Set(true) IsEnabled={afterEnable} (expect True)");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                "/Query /TN \"AvellSucks Autostart\" /XML")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var q = System.Diagnostics.Process.Start(psi)!;
            var xml = q.StandardOutput.ReadToEnd();
            q.WaitForExit();
            foreach (var marker in new[] { "<LogonTrigger>", "<RunLevel>HighestAvailable</RunLevel>", "<Command>" })
                Log($"autostart: task XML contains {marker} = {xml.Contains(marker, StringComparison.OrdinalIgnoreCase)}");
        }
        catch (Exception ex) { Log($"autostart: XML inspect failed {ex.Message}"); }
        AvellSucks.UI.Startup.AutoStart.Set(false);
        var afterDisable = AvellSucks.UI.Startup.AutoStart.IsEnabled();
        Log($"autostart: after Set(false) IsEnabled={afterDisable} (expect False)");
        Log($"autostart: RESULT {(afterEnable && !afterDisable ? "PASS" : "FAIL")}");
    }

    // Headless localization check: resolves representative keys under both
    // cultures through the real Loc provider, and confirms the system-culture
    // default maps correctly. Verified from the log, no UI automation needed.
    private static void RunI18nSelfTest()
    {
        string[] keys = { "Nav.Settings", "Nav.Fan", "Settings.HideOnMinimize", "Power.Saving.Tag", "Toast.Failed", "About.Tagline" };

        var loc = AvellSucks.UI.Localization.Loc.Instance;
        loc.Culture = new System.Globalization.CultureInfo("en");
        foreach (var k in keys) Log($"i18n EN  {k} = [{loc[k]}]");
        loc.Culture = new System.Globalization.CultureInfo("pt-BR");
        foreach (var k in keys) Log($"i18n PT  {k} = [{loc[k]}]");

        var sysDefault = AvellSucks.UI.Settings.SettingsStore.ResolveCulture(
            AvellSucks.UI.Settings.LanguagePreference.System);
        Log($"i18n InstalledUICulture={System.Globalization.CultureInfo.InstalledUICulture.Name} → System default resolves to {sysDefault.Name}");
        Log($"i18n settings path check: StartMinimized={AvellSucks.UI.Settings.SettingsStore.Current.Settings.StartMinimized}, " +
            $"HideOnMinimize={AvellSucks.UI.Settings.SettingsStore.Current.Settings.HideOnMinimize}, " +
            $"Language={AvellSucks.UI.Settings.SettingsStore.Current.Settings.Language}");
    }
}
