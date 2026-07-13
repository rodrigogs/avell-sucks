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

- Bind-only: server listens on localhost loopback.
- Loopback enforcement middleware rejects non-loopback before controller execution.
- Auth in MVP is loopback binding; future hardening may add mTLS or bearer tokens.
- When `GAMINGCENTER_REQUIRE_HTTPS=1`, plain HTTP returns `400 Bad Request`.
