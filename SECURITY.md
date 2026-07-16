# Security Policy

## Hardware risk (read this first)

AvellSucks writes reverse-engineered Embedded Controller (EC) registers and CPU
power limits at ring-0. **All registers were validated on a single machine — an
Avell laptop with an Intel i7-8750H.** On any other model the same EC address may
control something else; the read-back verification only confirms that the byte
landed, not that it was safe. There is **no warranty** (see the [Apache-2.0
license](LICENSE) and the README's Safety section). Running hardware writes on an
unverified machine can overheat, destabilize, or permanently damage it. Use at
your own risk.

Hardware writes are gated per surface:

- **WPF app:** writes are **on by default** — it's a control center for the machine
  it was built for. Turn them off in **Settings → Hardware writes** (a persisted
  per-user choice) for a read-only preview. The `GAMINGCENTER_ALLOW_EC_WRITES` env
  var force-overrides the toggle (`1` on, `0` off) and locks it.
- **Server / API and Core:** writes stay **off** unless `GAMINGCENTER_ALLOW_EC_WRITES=1`
  — the automation surface demands an explicit opt-in.

The registers were reverse-engineered on one machine (Avell i7-8750H); the writes
default on because that is the author's own hardware. On any other model, turn
writes off — the same EC address can mean something else.

Every write goes through: gate → allowlist (only known address/value pairs) →
before-snapshot → write → read-back verify → rollback on mismatch → JSONL audit.

## Network surface

The optional control server (`AvellSucks.Server`) can stay localhost-only or be
exposed on the network (LAN / Tailscale) and additionally host an MCP server at
`/mcp`. Exposure is governed by a **fail-closed** model:

- **Safe by default.** Out of the box it binds `127.0.0.1`, has no auth, does not
  allow remote writes, and does not run MCP. Nothing is exposed unless you turn it
  on in **Settings → Remote access**.
- **Loopback exempt, everything else must authenticate.** Requests from
  machine-local loopback (`127.0.0.0/8` and `::1`) pass without credentials. Every
  other origin — including IPv6 link-local (`fe80::/10`) — must present a valid
  bearer token and/or a client certificate (mTLS).
- **No auth configured ⇒ remote rejected.** If neither a token nor mTLS is set, a
  non-loopback request cannot authenticate and is denied — exposure can never be
  accidentally open.
- **Only the token hash is stored.** The bearer token is persisted and logged only
  as its SHA-256 hash and compared in constant time; the plaintext exists solely in
  the request header and momentarily in the UI when generated (shown once).
- **Remote writes are separately gated and off by default.** An authenticated
  remote client can read but cannot actuate fan/power unless `AllowRemoteWrites` is
  on — in addition to the existing local write gate. A remote-write denial is a
  distinct, truthful `403`, never a silent success.
- **mTLS fails closed.** With mTLS enabled and a configured CA thumbprint, a client
  certificate that does not match the thumbprint is rejected.
- The `/mcp` endpoint is behind the same authorization policy. Prefer a Tailscale
  address; do not bind `0.0.0.0` on an untrusted network.

Config lives in `%ProgramData%\AvellSucks\service.json` (hot-reloaded). The firewall
port stays closed unless you enable auto-open (or add the `netsh` rule manually).

## Reporting a vulnerability

This is a personal, unofficial project maintained by one person. If you find a
security issue (or a hardware-safety problem — e.g. an allowlist entry that can
damage a supported machine), please open a
[GitHub issue](https://github.com/rodrigogs/avell-sucks/issues) or, for something
sensitive, a private security advisory via the repository's **Security → Report a
vulnerability** tab. Best-effort response only; there is no SLA.
