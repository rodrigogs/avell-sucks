using System.Runtime.InteropServices;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using AvellSucks.Core.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvellSucks.Server.Hosting;

/// <summary>
/// Session 0 boot-restore for the Wi-Fi/Bluetooth radios. Runs once when the
/// AvellSucks service starts (typically at machine boot, as LocalSystem, before
/// any user logs in). Reads the persisted user intent and, if the radios are
/// currently OFF, re-enables them via <see cref="IMachineControlService"/>.
///
/// Safety contract (mirrors <see cref="WirelessBootRestorePolicy"/>):
/// <list type="bullet">
/// <item>One-directional: it only ever turns radios ON, never OFF.</item>
/// <item>Honors a dedicated env gate (<c>GAMINGCENTER_RESTORE_RADIOS</c>) that is
///   independent of the remote-write gate, so a restore never opens the network
///   API to writes.</item>
/// <item>Constructs its OWN permissive write gate for the single restore call —
///   the service-wide <see cref="WriteGate"/> may be closed by default and must
///   not be force-opened for the lifetime of the host.</item>
/// <item>Bounded by a hard timeout so a stuck WMI/EC call can never block the
///   control API from coming up.</item>
/// <item>Never throws; every outcome is traced to a durable log + ILogger.</item>
/// </list>
/// </summary>
public sealed class WirelessBootRestoreService : IHostedService
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(45);

    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<NetworkServiceConfig> _config;
    private readonly ILogger<WirelessBootRestoreService> _logger;

    public WirelessBootRestoreService(
        IServiceProvider services,
        IOptionsMonitor<NetworkServiceConfig> config,
        ILogger<WirelessBootRestoreService> logger)
    {
        _services = services;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only run the restore when the process is hosted by the SCM (Session 0,
        // no interactive desktop). In console/dev the UI owns the EC and the
        // Server is a diagnostic process — a duplicate restore there could bypass
        // the dev's (usually closed) write gate.
        if (!IsRunningAsWindowsService())
        {
            Trace("Skipped: not running as a Windows Service (console/dev).");
            return;
        }

        // Bound the whole restore attempt so a pathological WMI/EC hang can never
        // keep the control API from declaring the host started.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(HardTimeout);
        var token = bound.Token;

        var restoreEnabled = IsRestoreGateEnabled();
        var configFlag = _config.CurrentValue.RestoreWirelessRadiosOnBoot;

        MachineControlStatus? status = null;
        var registered = _services.GetService(typeof(IMachineControlService)) as IMachineControlService;
        try
        {
            if (registered is not null)
                status = await registered.GetStatusAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Trace($"Status probe timed out after {HardTimeout.TotalSeconds:F0}s; leaving radios untouched.");
            return;
        }
        catch (Exception ex)
        {
            Trace($"Status probe failed: {ex.GetType().Name}: {ex.Message}");
        }

        var decision = WirelessBootRestorePolicy.Decide(configFlag, restoreEnabled, status);
        Trace($"Decision: {decision.Reason}");
        _logger.LogInformation("Wireless boot-restore: {Reason}", decision.Reason);

        if (!decision.ShouldRestore)
            return;

        try
        {
            var reader = _services.GetRequiredService<IEcBackend>();
            var writer = _services.GetRequiredService<IEcWriter>();
            var platform = _services.GetRequiredService<IPlatformMachineControlBackend>();
            var audit = _services.GetRequiredService<IMachineControlAuditLog>();

            // One-shot permissive gate for the local restore only. The network
            // API's own MachineControlService stays bound to the service-wide
            // (default-closed) gate — this never opens remote writes.
            var restorer = new MachineControlService(
                reader, writer, platform, new WriteGate(allowWrites: true), audit);

            var result = await restorer.SetWirelessRadiosAsync(
                enabled: true, reason: "service:boot-restore", token).ConfigureAwait(false);
            Trace($"Restore outcome: {result.Outcome} — {result.Message}");
            _logger.LogInformation("Wireless boot-restore: {Outcome} — {Message}", result.Outcome, result.Message);
        }
        catch (Exception ex)
        {
            // Never let a restore failure take the host down. The API stays up.
            Trace($"Restore threw: {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "Wireless boot-restore threw; the service continues normally.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Dedicated env kill-switch independent of the remote-write gate. Defaults
    /// to enabled (the restore is opt-in via the config flag); set
    /// <c>GAMINGCENTER_RESTORE_RADIOS=0</c> to forcibly suppress it.
    /// </summary>
    internal static bool IsRestoreGateEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("GAMINGCENTER_RESTORE_RADIOS");
        return raw is null || raw is "1" or "true" or "TRUE" or "True";
    }

    /// <summary>
    /// Canonical "are we a Windows Service" signal: SCM-launched processes run
    /// in Session 0 with no interactive desktop, so UserInteractive is false.
    /// </summary>
    private static bool IsRunningAsWindowsService()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Environment.UserInteractive;

    /// <summary>
    /// Best-effort durable trace so a missing restore at boot is diagnosable even
    /// though a Session 0 service has no console. Appends to
    /// <c>%ProgramData%\AvellSucks\boot-restore.log</c>. Never throws.
    /// </summary>
    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(ServiceConfigPaths.Dir, "boot-restore.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { /* diagnostics must never break the service */ }
    }
}
