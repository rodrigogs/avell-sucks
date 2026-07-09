using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;

namespace AvellSucks.UI.Services;

/// <summary>
/// Append-only JSONL audit sink for EC writes, local to the UI (the Server has
/// its own copy; the UI must not depend on the Server assembly). One JSON object
/// per line; failures to write are swallowed so auditing never breaks a write.
/// </summary>
internal sealed class JsonlAuditLog : IWriteAuditLog
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    private readonly string _path;
    private readonly object _lock = new();

    public JsonlAuditLog(string path)
    {
        _path = path;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch { /* best-effort */ }
    }

    public ValueTask RecordAsync(EcWriteResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            var line = JsonSerializer.Serialize(result, s_json);
            lock (_lock) { File.AppendAllText(_path, line + Environment.NewLine); }
        }
        catch { /* auditing must never break a write */ }
        return ValueTask.CompletedTask;
    }
}

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
            var allow = WriteGateInfo.EcWritesEnabled;
            var gate = new WriteGate(allow);
            App.Trace($"HardwareServices: elevated={elevated} writesEnabled={allow} " +
                      $"(env GAMINGCENTER_ALLOW_EC_WRITES='{Environment.GetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES") ?? "(unset)"}')");

            // Writes-enabled policy lives in WriteGateInfo: elevated ⇒ on by
            // default, unless the env var explicitly forces it off. Only stand up
            // the real WMI pipeline when writes are enabled.
            if (!allow)
            {
                App.Trace("HardwareServices: STUB path — writes disabled (not elevated, or forced off). No hardware writes, no reconciler.");
                return;
            }

            try
            {
                var backend = new WmiEcBackend();
                var auditDir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
                    ?? AppPaths.AuditDir;
                var audit = new JsonlAuditLog(Path.Combine(auditDir, "ec-write-audit.jsonl"));
                var writer = new SafeEcWriter(gate, new EcWriteAllowlist(), backend, backend, audit);

                s_gate = gate;
                s_backend = backend;
                s_writer = writer;

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
                s_backend = null; s_writer = null; s_gate = null;
            }
        }
    }
}
