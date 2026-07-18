using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;

namespace AvellSucks.UI.Services;

/// <summary>
/// Minimal composition root for the hardware service layer (pre-DI). Builds the
/// real EC pipeline once and hands out service instances. When writes are
/// disabled OR the WMI backend can't be reached, callers fall back to the stub
/// so the app still runs anywhere.
///
/// This is the seam the DESIGN.md "Reactive Architecture Spec" Phase 1 replaces
/// with Microsoft.Extensions.DependencyInjection; for now it centralizes the
/// stub-vs-real choice in one place instead of scattered `new LocalFanService()`.
/// </summary>
public static class HardwareServices
{
    private static readonly object s_lock = new();
    private static bool s_built;
    private static WmiEcBackend? s_backend;
    private static SafeEcWriter? s_writer;
    private static WriteGate? s_gate;
    private static IMachineControlService? s_machineControls;

    /// <summary>
    /// The fan service: real (WMI-backed) when EC writes are enabled and the
    /// backend constructs; the in-memory stub otherwise.
    /// </summary>
    public static IFanService CreateFanService()
    {
        EnsurePipeline();
        if (s_backend is not null && s_writer is not null && s_gate is not null)
        {
            try { return new WmiFanService(s_backend, s_writer, s_gate); }
            catch { /* fall through to stub */ }
        }
        return new LocalFanService();
    }

    /// <summary>
    /// The real EC read backend, or null when unavailable (writes off / no WMI).
    /// Used by reconcilers (e.g. <see cref="FanStateMonitor"/>) that poll device state.
    /// </summary>
    public static IEcBackend? EcBackend()
    {
        EnsurePipeline();
        return s_backend;
    }

    /// <summary>
    /// Power service: real (WMI-backed, confirmed PL registers 0x783/0x784/0x785)
    /// when EC writes are enabled and the backend constructs; stub otherwise.
    /// </summary>
    public static IPowerService CreatePowerService()
    {
        EnsurePipeline();
        if (s_backend is not null && s_writer is not null && s_gate is not null)
        {
            try { return new WmiPowerService(s_backend, s_writer, s_gate); }
            catch { /* fall through to stub */ }
        }
        return new LocalPowerService();
    }

    /// <summary>RGB stays on the stub: no HID backend implemented yet.</summary>
    public static IRgbService CreateRgbService() => new LocalRgbService();

    /// <summary>
    /// Model-specific radio/touchpad/webcam/panel controls, or null when the app
    /// is not elevated and therefore cannot safely expose the real backend.
    /// </summary>
    public static IMachineControlService? MachineControls()
    {
        EnsurePipeline();
        return s_machineControls;
    }

    /// <summary>True once the real WMI pipeline is live (diagnostics/UX may query).</summary>
    public static bool IsRealBackendActive { get { EnsurePipeline(); return s_backend is not null; } }

    private static void EnsurePipeline()
    {
        if (s_built) return;
        lock (s_lock)
        {
            if (s_built) return;
            s_built = true;

            var elevated = WriteGateInfo.IsElevated();
            // The gate re-reads the policy on every write, so flipping the Settings
            // toggle enables/disables writes live — no pipeline rebuild, no restart.
            var gate = new WriteGate(() => WriteGateInfo.EcWritesEnabled);
            App.Trace($"HardwareServices: elevated={elevated} writesEnabled(now)={WriteGateInfo.EcWritesEnabled} " +
                      $"(env GAMINGCENTER_ALLOW_EC_WRITES='{Environment.GetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES") ?? "(unset)"}')");

            // Build the REAL backend whenever we're elevated: reads/telemetry and the
            // reconcilers must reflect true hardware state even in read-only (writes-
            // off) mode — showing stub data while claiming to be live would violate
            // the app's honesty principle. The gate (above) governs WRITES only; a
            // write while the toggle is off is denied+audited by SafeEcWriter.
            if (!elevated)
            {
                App.Trace("HardwareServices: STUB path — not elevated. No real EC access (reads or writes).");
                return;
            }

            try
            {
                var backend = new WmiEcBackend();
                var auditDir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
                    ?? AppPaths.AuditDir;
                // UI: swallow audit-write errors so logging can't break a hardware write.
                var audit = new JsonlAuditLog(Path.Combine(auditDir, "ec-write-audit.jsonl"), swallowWriteErrors: true);
                var writer = EcPipeline.BuildWriter(backend, backend, gate, audit);
                var machineAudit = new JsonlMachineControlAuditLog(
                    Path.Combine(auditDir, "machine-control-audit.jsonl"), swallowWriteErrors: true);
                var machineControls = new MachineControlService(
                    backend, backend, new WindowsMachineControlBackend(), gate, machineAudit);

                s_gate = gate;
                s_backend = backend;
                s_writer = writer;
                s_machineControls = machineControls;

                // Diagnostic-only reachability probe — fire-and-forget OFF the
                // calling thread. It used to block here with .GetResult(), and
                // because CreateFanService() runs in a view field initializer
                // during `new MainWindow()` (before .Show()), a cold root\WMI query
                // (tens–hundreds of ms) delayed the window appearing. It only logs.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var probe = await backend.ReadSnapshotAsync([1873]).ConfigureAwait(false);
                        var f = probe.Fields.Count > 0 ? probe.Fields[0] : null;
                        App.Trace(f is { Ok: true }
                            ? $"HardwareServices: REAL path OK — EC 0x751 read = {f.Value} (elevated & WMI reachable)."
                            : $"HardwareServices: REAL path built but EC read FAILED — {f?.Error ?? "no field"}. Likely not elevated.");
                    }
                    catch (Exception ex) { App.Trace($"HardwareServices: REAL path EC probe threw — {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                App.Trace($"HardwareServices: pipeline build failed → STUB. {ex.Message}");
                s_backend = null; s_writer = null; s_gate = null; s_machineControls = null;
            }
        }
    }
}
