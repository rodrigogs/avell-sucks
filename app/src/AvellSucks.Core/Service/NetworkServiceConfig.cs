namespace AvellSucks.Core.Service;

/// <summary>
/// Network exposure + auth configuration for the AvellSucks control service.
/// Written by the elevated UI to %ProgramData%\AvellSucks\service.json and
/// hot-reloaded by the service. Plain data — no behavior — so it serializes
/// cleanly (System.Text.Json Web / camelCase). Defaults are the safe baseline:
/// loopback-only, HTTP, no auth, no remote writes, MCP off, firewall untouched.
/// </summary>
public sealed class NetworkServiceConfig
{
    /// <summary>Kestrel bind address. "127.0.0.1" (default) = loopback only.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>TCP port. Default 5055 (unchanged from the console MVP).</summary>
    public int Port { get; set; } = 5055;

    /// <summary>"http" (default) or "https".</summary>
    public string Scheme { get; set; } = "http";

    /// <summary>Absolute path to the PFX cert used when <see cref="Scheme"/> is "https".</summary>
    public string? HttpsCertPath { get; set; }

    /// <summary>Authentication settings applied to non-loopback callers.</summary>
    public AuthConfig Auth { get; set; } = new();

    /// <summary>
    /// Second gate for hardware WRITES from non-loopback callers. Off by default:
    /// even an authenticated remote client cannot actuate fan/power unless this is
    /// on (in addition to the existing WriteGate). Loopback writes ignore this.
    /// </summary>
    public bool AllowRemoteWrites { get; set; }

    /// <summary>Serve the MCP endpoint at /mcp when true.</summary>
    public bool McpEnabled { get; set; }

    /// <summary>When true, the service opens the Windows Firewall port on start.</summary>
    public bool FirewallAutoOpen { get; set; }

    /// <summary>
    /// When true and the machine is the verified Avell 1555, the service
    /// re-enables the Wi-Fi + Bluetooth radios on startup (and after a reboot)
    /// when they are currently OFF. Default false: nothing is touched unless the
    /// user explicitly turned wireless on from the UI and it was verified.
    ///
    /// <b>One-directional:</b> the restore path only ever calls
    /// <c>SetWirelessRadiosAsync(true)</c>. It never turns radios OFF. A missing
    /// or false flag means the service leaves the radios exactly as the EC/BIOS
    /// left them at boot.
    ///
    /// Rationale: the EC forgets the radio state byte across a cold boot, and on
    /// the G1555 the radios can come up hardware-gated (Code 22) even when the
    /// user had them on. Persisting the verified-on intent lets the Session 0
    /// service (no login required) reconcile them to the user's last state.
    /// </summary>
    public bool RestoreWirelessRadiosOnBoot { get; set; }
}

/// <summary>Authentication configuration. Required for non-loopback callers.</summary>
public sealed class AuthConfig
{
    /// <summary>
    /// SHA-256 hex of the shared bearer token. Null = no bearer configured.
    /// The plaintext token is never stored — only this hash.
    /// </summary>
    public string? BearerTokenSha256 { get; set; }

    /// <summary>Require/accept a client certificate (mTLS).</summary>
    public bool MtlsEnabled { get; set; }

    /// <summary>Thumbprint of the CA/cert a client certificate must chain to / match.</summary>
    public string? MtlsCaThumbprint { get; set; }
}
