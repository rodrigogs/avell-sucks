# Network service — elevated validation runbook

The wire-level security of the network API + MCP is covered by automation
(`scripts/smoke-network-api.sh`, run on the machine): it proves loopback read/MCP,
MCP-absent-when-disabled, fail-closed auth on a non-loopback bind, and the
remote-write gate (403 with no hardware contact).

What that smoke test can **not** cover needs an **elevated** Windows session
(installing the service, opening the firewall, and the real TLS/mTLS handshake).
Run the checklist below from an elevated PowerShell/terminal and tick each item.

## What's already been validated elevated

- **Windows Service lifecycle:** `sc create`/`start`/`stop`/`delete` all succeed;
  the API answers `GET /api/system/snapshot` → 200 while running as a service.
- **Service account reaches WMI/EC** (resolves the design's open item): with the
  default `sc create` account (`LocalSystem`), the service served
  `GET /api/fan/diagnostic` and `/api/fan/mode` → 200 with real EC register bytes.
  No custom service account is needed; `LocalService`/`NetworkService` were not
  required.
- **Firewall rule add/remove** via `netsh advfirewall` works.
- **Config file + directory ACL:** `icacls` shows admin/SYSTEM full control and
  Everyone read-only; a non-admin user cannot overwrite `service.json`.
- **HTTPS binds** with a passwordless self-signed PFX (the shape
  `SelfSignedCertFactory` produces).
- **mTLS fail-closed (negative path):** on a non-loopback bind with `mtlsEnabled`,
  a client presenting no client certificate is denied (403).
- **mTLS product bug fixed:** `AddCertificate` used to reject a self-signed client
  cert (the shape the app's own UI generates) before the thumbprint pin ran. Fixed
  so `AllowedCertificateTypes=All` + `ValidateCertificateUse=false` let the cert
  through and the thumbprint pin stays the fail-closed decider
  (`CertificateThumbprint.Matches`, unit-tested).

The one path not proven automatically is the **mTLS positive handshake**
(correct cert → 200): a client cert must be one the OS TLS stack will actually
present, which for self-signed certs needs a local test CA trusted by the client.
That's the machine-trust-store change in §5 — do it manually.

## Preconditions

- [ ] Server published locally on the machine (build output copied to a local path
      such as `C:\Temp\avs-server\`). Running the server from a WSL UNC path
      (`\\wsl.localhost\...`) adds ~9 s startup and may fail to bind — copy the
      publish output to a local Windows path first.
- [ ] The WPF UI is built and runnable (it is the elevated writer of `service.json`).
- [ ] A second device on the same Tailscale tailnet (or LAN) for real remote access,
      with `curl`/PowerShell.
- [ ] OEM Gaming Center closed; laptop on AC (only relevant if you exercise a real
      remote WRITE, which actuates hardware).

## 1. Config ACL (non-admin cannot subvert auth)

The elevated UI writes `%ProgramData%\AvellSucks\service.json` and applies an
admin-write / world-read ACL (`ConfigFileSecurity.Harden`).

- [ ] From the elevated UI: **Settings → Remote access**, generate a token / flip
      any toggle so the config is written.
- [ ] Inspect the DACL: `icacls C:\ProgramData\AvellSucks\service.json`
      — expect Administrators `(F)`, SYSTEM `(F)`, Everyone `(R)`, and **no** write
      for standard Users.
- [ ] As a **standard (non-admin) user**, try to overwrite the file:
      `Set-Content C:\ProgramData\AvellSucks\service.json '{}'` → must fail with
      access denied. Proves a non-admin can't turn on `allowRemoteWrites` or swap
      the token hash.

## 2. Windows Service lifecycle (sc.exe via the UI)

- [ ] **Settings → Remote access → Run as a background service** ON → confirm
      `sc query AvellSucksControl` shows the service and it reaches `RUNNING`.
- [ ] Toggle OFF → `sc query AvellSucksControl` → `1060` (not installed).
- [ ] Service account: the default (`LocalSystem`) reaches WMI/EC. If you switch it
      (`sc config AvellSucksControl obj= ...`), re-test an EC read afterward.

## 3. Firewall auto-open

- [ ] With exposure on and **Open firewall automatically** ON, start the service →
      `netsh advfirewall firewall show rule name="AvellSucks Control Service"`
      shows an inbound allow for the configured port.
- [ ] Turn the toggle OFF (or uninstall) → the rule is removed.
- [ ] With auto-open OFF, the manual command works instead:
      `netsh advfirewall firewall add rule name="AvellSucks Control Service" dir=in action=allow protocol=TCP localport=5055`

## 4. Real remote access over Tailscale

- [ ] Bind the Tailscale IP (Settings → Remote access → address picker), HTTP,
      generate a token, MCP ON.
- [ ] From the **second device**:
      `curl http://<tailscale-ip>:5055/api/system/snapshot` (no token) → 401/403;
      with `-H "Authorization: Bearer <token>"` → 200.
- [ ] MCP: point an MCP client (or `curl` an `initialize` JSON-RPC POST with the
      bearer header) at `http://<tailscale-ip>:5055/mcp` → 200; without the token → 401/403.

## 5. HTTPS + mTLS handshake (the part unit tests can't reach)

`CertificateThumbprint.Matches` is unit-tested; the actual TLS client-cert
handshake is not.

- [ ] Settings → Remote access → **Use HTTPS** ON (generates a self-signed PFX at
      `%ProgramData%\AvellSucks\listener.pfx`). Restart the service (scheme change
      needs a rebind).
- [ ] `curl -k https://<tailscale-ip>:5055/api/system/snapshot -H "Authorization: Bearer <token>"` → 200 over TLS.
- [ ] Enable **mTLS**, set the allowed client-cert thumbprint, restart.
      - [ ] A client presenting the **matching** client cert authenticates (200).
            (Self-signed client certs need a local test CA trusted by the client —
            e.g. issue server+client certs from a CA installed into the client's
            trust store — because the OS TLS stack won't present an untrusted-chain
            self-signed client cert.)
      - [ ] A wrong/absent cert with a valid bearer token still works when bearer is
            also configured (both-accepted); with bearer cleared (cert-only), the
            wrong/absent cert is rejected.
      - [ ] `mtlsEnabled: true` with an **empty** thumbprint rejects every client
            cert (fail-closed — `OnCertificateValidated` ctx.Fail).

## 6. Hot-reload boundary

- [ ] Service running. Flip `allowRemoteWrites` from the UI → without restart, a
      remote authenticated WRITE goes 403 → 200 (or back). Confirms `IOptionsMonitor`
      hot-reload for the live toggles.
- [ ] Change `bindAddress`/`port` → takes effect only after a service restart
      (documented boundary).

## Record

- [ ] Note pass/fail per item and the chosen service account. File any defect as its
      own issue; the wire-level fail-closed behavior is already proven by
      `scripts/smoke-network-api.sh`.
