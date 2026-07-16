# AvellSucks local API

The **optional** loopback control server (`AvellSucks.Server`). The WPF app does
not need it — it drives hardware in-process. Run the server only for
automation/scripting.

Base URL: `http://127.0.0.1:5055/`
Secure alternative: `https://127.0.0.1:5055/` with `GAMINGCENTER_REQUIRE_HTTPS=1`.
OpenAPI: `/openapi/v1.json` is served via `MapOpenApi()` (no Swagger UI).
Events: text/event-stream at `/events`.

## Events

- `/events` is always text/event-stream SSE. It never completes; clients should disconnect when done.
- The server sends one event per second.
- Reconnection is not automated.

## Endpoints

### `GET /`

Health/branding.

Response:
```http
HTTP/1.1 200 OK
XLocalApi: True
Content-Type: text/plain; charset=utf-8

AvellSucks local API
```
> Note: the middleware currently emits the header as `XLocalApi: True` (see
> `Server/Program.cs`), not the more conventional `X-Local-Api: true`.

### `GET /api/system/snapshot`

System snapshot for automation/scripts.

Request:
```http
GET /api/system/snapshot HTTP/1.1
Host: 127.0.0.1:5055
Accept: application/json
```

Response:
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "timestamp": "2026-07-03T20:00:00Z",
  "totalMemoryBytes": 123456,
  "freeMemoryBytes": 0,
  "activeProcessCount": 142,
  "processes": [
    {
      "id": 1,
      "name": "System Idle Process",
      "memoryBytes": 4096,
      "cpuPercent": null
    }
  ]
}
```

Notes:
- `cpuPercent` is null in the MVP because deriving CPU % from multi-sample `TimeSpan` is not implemented yet.
- Each process object also carries a `"path"` field (currently always `null`).

### `GET /api/fan/mode`

Current fan mode, interpreted from the EC control byte (`FanController`).
Returns a `FanMode` (`auto` / `boost` / `custom` / `L1`..`L5`).

### `GET /api/fan/curve`

The five temp→PWM custom-curve levels. Returns `{ "levels": [ { "temperatureC", "pwm", "address" } ] }`.

### `GET /api/fan/diagnostic`

Raw EC snapshot of the fan registers (an `EcSnapshot`), for debugging.

### `POST /api/fan/mode`

Body `{ "mode": "boost" }` — one of `auto`, `boost`, `custom`, `L1`..`L5`.
Unknown modes → `400`. Returns the `EcWriteResult`; gate-blocked → `403`,
read-back-verify failure → `500`.

### `POST /api/fan/curve`

Body `{ "levels": [ { "temperatureC", "pwm", "address" }, ... ] }` — exactly five
levels; temperatures 30–100 °C, strictly ascending; PWM 0–140 (`0x8C`). Writes
each level then flips the mode to custom; returns a `BatchWriteResultDto`. Invalid
curve → `400`, gate-blocked → `403`.

### `GET /api/power/profile`

Current CPU power-limit profile (PL1/PL2/PL4 watts) from the EC — a
`PowerProfileState`. Unsupported hardware → `501`; degraded read → `503`.

### `POST /api/power/profile`

Body `{ "pl1": 45, "pl2": 90, "pl4": 107 }` — any subset; omitted fields are left
unchanged. Byte-valued watts (0–254). Returns a `BatchWriteResultDto`;
gate-blocked → `403`. An empty body (no change) returns an empty result.

> All fan/power writes go through the same `SafeEcWriter` pipeline (gate →
> allowlist → read-back verify → rollback → audit) and require the write gate open
> (`GAMINGCENTER_ALLOW_EC_WRITES=1` for the server).

### Error schema

Unless otherwise noted, non-success responses return plain text bodies:
```http
HTTP/1.1 <status>
Content-Type: text/plain; charset=utf-8

<reason>
```

## Auth / safety

The server can be exposed on the network (LAN / Tailscale) behind a **fail-closed**
authentication model. The rules:

- **Loopback is exempt.** A request from `127.0.0.0/8` or `::1` is machine-local
  and passes without credentials — local automation keeps working unchanged.
- **Non-loopback requires a credential.** A remote caller must present a valid
  `Authorization: Bearer <token>` **and/or** a client certificate (mTLS). The
  bearer token is matched by constant-time compare against the SHA-256 hash in the
  config; only that hash is ever stored or logged (never the plaintext token).
- **No auth configured ⇒ remote is rejected.** If neither a bearer token nor mTLS
  is configured, a non-loopback request cannot authenticate and is denied. Network
  exposure can never be accidentally open.
- **IPv6 link-local is REMOTE.** Only machine-local loopback (`127.0.0.0/8` and
  `::1`, exactly what `IPAddress.IsLoopback` covers) is exempt. An `fe80::/10`
  link-local neighbor is a different machine on the same link and must
  authenticate — `CallerInfo.IsLoopback` gates authentication itself, so it does
  not exempt link-local.
- **Remote writes are separately gated.** Hardware **writes** (fan/power) from a
  non-loopback caller pass the existing `WriteGate` **and** a second remote-write
  gate. That gate is **off by default**: an authenticated remote client can read
  freely but cannot actuate fan/power unless `AllowRemoteWrites` is on. A
  remote-write denial is a distinct, truthful `403` (separate from a
  `WriteGate`-closed `403`), never a silent success.
- **mTLS fails closed.** When mTLS is enabled with a configured CA thumbprint, a
  client certificate whose thumbprint does not match is rejected.
- When `GAMINGCENTER_REQUIRE_HTTPS=1` (or `Scheme = "https"` in config), plain
  HTTP returns `400 Bad Request`.

### Configuration

Network/auth settings live in a hot-reloaded JSON file the elevated WPF UI writes:

```
%ProgramData%\AvellSucks\service.json
```

Keys: `bindAddress`, `port`, `scheme` (`http`/`https`), `httpsCertPath`,
`auth.bearerTokenSha256`, `auth.mtlsEnabled`, `auth.mtlsCaThumbprint`,
`allowRemoteWrites`, `mcpEnabled`, `firewallAutoOpen`. Auth / remote-write / MCP
toggles apply live on save; changing the bind address/port/scheme needs a service
restart. Edit it from **Settings → Remote access** in the app rather than by hand.

### Authenticated request example

```http
POST /api/fan/mode HTTP/1.1
Host: 100.72.1.5:5055
Authorization: Bearer <token>
Content-Type: application/json

{ "mode": "boost" }
```

With `allowRemoteWrites: false` this returns `403` with the remote-write reason;
with it on (and the local `WriteGate` open) it actuates and the audit line records
`"origin":"remote 100.72.1.5 via Bearer"`.

## MCP endpoint

When `mcpEnabled` is on, the server hosts a Model Context Protocol server over
**Streamable HTTP** at `/mcp`, under the **same** authentication model as the REST
API (loopback exempt; remote requires bearer and/or mTLS). Tools:

- `get_system_snapshot`, `get_fan_mode`, `get_power_profile` — reads, available to
  any authenticated caller.
- `set_fan_mode`, `set_power_profile` — writes, gated by the remote-write policy.
  A blocked write returns a truthful denial message; the tool never claims a gated
  write succeeded.
