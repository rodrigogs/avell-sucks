using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AvellSucks.UI.Services;

/// <summary>
/// Reconciles the verified-on wireless intent after Windows resumes from sleep.
/// The logon autostart task does not run again on resume, but the resident tray
/// application receives <see cref="SystemEvents.PowerModeChanged"/>.
/// </summary>
public static class WirelessResumeRestorer
{
    private static readonly SemaphoreSlim s_reconcileGate = new(1, 1);
    private static bool s_started;

    /// <summary>Subscribe once for the lifetime of the resident WPF application.</summary>
    public static void Start()
    {
        if (s_started) return;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        s_started = true;
        App.Trace("WirelessResumeRestorer: subscribed to Windows power-resume events.");
    }

    /// <summary>Unsubscribe during app shutdown so a static OS event cannot retain the app.</summary>
    public static void Stop()
    {
        if (!s_started) return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        s_started = false;
    }

    /// <summary>
    /// Reconcile the radios using the same verified-on intent as the Session 0
    /// boot restore. This method is also the headless CLI entry point, allowing a
    /// scheduled/manual diagnostic invocation to exercise the exact wake path.
    /// </summary>
    public static async Task ReconcileAsync(string reason, bool waitForDevices)
    {
        if (!await s_reconcileGate.WaitAsync(0).ConfigureAwait(false))
        {
            App.Trace($"WirelessResumeRestorer: skipped {reason}; a reconciliation is already running.");
            return;
        }

        try
        {
            if (waitForDevices)
            {
                // Resume is announced before PCI/USB re-enumeration necessarily
                // completes. Let the Intel CNVi function settle before PnP rescan.
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }

            var config = new ServiceConfigManager().Load();
            if (!config.RestoreWirelessRadiosOnBoot)
            {
                App.Trace($"WirelessResumeRestorer: skipped {reason}; verified-on wireless intent is not set.");
                return;
            }

            var controls = HardwareServices.MachineControls();
            if (controls is null)
            {
                App.Trace($"WirelessResumeRestorer: skipped {reason}; machine controls are unavailable.");
                return;
            }

            var status = await controls.GetStatusAsync().ConfigureAwait(false);
            if (!status.SupportedMachine)
            {
                App.Trace($"WirelessResumeRestorer: skipped {reason}; machine model is not verified.");
                return;
            }

            if (status.WirelessRadiosEnabled is null)
            {
                App.Trace($"WirelessResumeRestorer: skipped {reason}; wireless EC state is unknown ({status.Error ?? "no detail"}).");
                return;
            }

            // Reassert even when the EC still says ON. The Intel function can be
            // left with a bad PnP/driver state after resume (for example Code 31)
            // while the EC state byte remains enabled; SetWirelessRadiosAsync(true)
            // performs the required Windows PnP rescan/reconciliation as well as
            // the idempotent EC state+trigger sequence.
            if (status.WirelessRadiosEnabled == true)
                App.Trace($"WirelessResumeRestorer: {reason}; EC reports ON, reapplying Windows reconciliation.");

            var result = await controls.SetWirelessRadiosAsync(true, reason).ConfigureAwait(false);
            App.Trace($"WirelessResumeRestorer: {reason} outcome={result.Outcome} verified={result.Verified} message={result.Message ?? "none"}");
        }
        catch (Exception ex)
        {
            // A power notification is raised on an OS callback thread; it must
            // never terminate the resident app if WMI/EC access is unavailable.
            App.Trace($"WirelessResumeRestorer: {reason} threw (ignored) — {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            s_reconcileGate.Release();
        }
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _ = ReconcileAsync("ui:power-resume", waitForDevices: true);
    }
}
