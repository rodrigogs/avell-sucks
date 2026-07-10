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

Hardware writes are **off by default everywhere** and opt-in:

- **Server / API and Core:** writes stay off unless `GAMINGCENTER_ALLOW_EC_WRITES=1`.
- **WPF app:** reading/telemetry works when elevated, but writes stay off until you
  turn them on in **Settings → Hardware writes** (a persisted per-user choice). The
  `GAMINGCENTER_ALLOW_EC_WRITES` env var force-overrides the toggle (`1` on, `0`
  off) and locks it.

Every write goes through: gate → allowlist (only known address/value pairs) →
before-snapshot → write → read-back verify → rollback on mismatch → JSONL audit.

## Network surface

The optional local API (`AvellSucks.Server`) binds **127.0.0.1 only** and rejects
any non-loopback client. It is a separate, opt-in process; the WPF app does not
start it. There is no authentication beyond the loopback restriction — do not
expose it beyond localhost (e.g. via a reverse proxy or port forward).

## Reporting a vulnerability

This is a personal, unofficial project maintained by one person. If you find a
security issue (or a hardware-safety problem — e.g. an allowlist entry that can
damage a supported machine), please open a
[GitHub issue](https://github.com/rodrigogs/avell-sucks/issues) or, for something
sensitive, a private security advisory via the repository's **Security → Report a
vulnerability** tab. Best-effort response only; there is no SLA.
