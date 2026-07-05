# OpenAPI-ish notes — GamingCenter local API

Base URL: `http://127.0.0.1:5055/`
Secure alternative: `https://127.0.0.1:5055/` with `GAMING_CENTER_REQUIRE_HTTPS=1`.
Documentation: `/swagger` and `/openapi/v1.json` are served when the app is built with `Microsoft.AspNetCore.OpenApi`.
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
X-Local-Api: true
Content-Type: text/plain; charset=utf-8

GamingCenter local API
```

### `POST /api/hello`

Human/script-facing automation surface.

Request:
```http
POST /api/hello HTTP/1.1
Host: 127.0.0.1:5055
Content-Type: application/json

{"name":"MVP"}
```

Responses:
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "message": "Hello, MVP!",
  "requestedAt": "2026-07-03T20:00:00Z"
}
```

Errors:
- `415 Unsupported Media Type`: `Content-Type` is not `application/json`.

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
- When `GAMING_CENTER_REQUIRE_HTTPS=1`, plain HTTP returns `400 Bad Request`.
