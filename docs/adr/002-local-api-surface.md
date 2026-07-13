# ADR-002 Local API Surface

## Status

**Accepted** — supersedes the optional named-pipe gRPC path unless future privilege separation demands it.

## Context

The MVP needs a local automation API between the Windows service core and one or more local clients (human/scripts, Avalonia UI). It must be safe to enable by default on developer and end-user machines, simple to operate, and sufficient for telemetry and control.

Options considered:

1. **Loopback HTTPS REST + SSE/WebSocket events** (chosen)
2. Named-pipe gRPC only for privileged local UI↔service IPC

## Decision

Use a single loopback ASP.NET Core server with:

- **REST** over `http://127.0.0.1:{PORT}` for requests (HTTPS is opt-in; loopback-only binding is the primary boundary)
- **Server-Sent Events (SSE)** at `/events` for telemetry
- **Bind enforcement**: validate `HttpContext.Connection.RemoteIpAddress` is loopback or reversible connections require client certs.
- **No external network exposure**: no binding to `0.0.0.0`/`*`; Kestrel is configured to loopback only.
- **Windows-only hardware boundary**: hardware access remains in Windows code paths.

Named-pipe gRPC is deferred until privileged/local-UI separation ships.

## Rationale

- Easier smoke testing with `curl`/web browsers.
- SSE has broad client support and avoids persistent TCP socket bookkeeping in the MVP.
- Localhost HTTPS provides canonical auth material for future mutual TLS or bearer tokens without bombing complexity.
- Named pipes would add OS-specific transport code early; keep the bar to reintroduce low.

## Security Notes

- Loopback enforcement via middleware (`src/AvellSucks.Server/Middleware/EnforceLoopbackMiddleware.cs`, wired by the `UseLoopbackOnly` extension; non-localhost → 403).
- HTTPS is **off by default**; the server binds plain `http://127.0.0.1:5055`. HTTPS is opt-in via `GAMINGCENTER_REQUIRE_HTTPS=1` (dev cert / pinned cert), since loopback binding is already the primary protection.
- Do not open additional ports without a corresponding ADR.

## Alternatives

- **gRPC over named pipes** — rejected for MVP due to client friction, but tracked as a future ADR.
- **HTTPS-by-default over loopback** — considered, but **not** the shipped default:
  loopback binding is already the primary boundary, so the server ships plain
  `http://127.0.0.1:5055` and HTTPS is opt-in via `GAMINGCENTER_REQUIRE_HTTPS=1`
  (keeps `curl`/browser smoke-testing frictionless; a dev cert isn't needed to run).
