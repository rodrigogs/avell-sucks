# Architecture Decision Records

Historical decision log. The *current* architecture is [`../ARCHITECTURE.md`](../ARCHITECTURE.md); ADRs are kept for the reasoning, even where later work amended them.

| ADR | Title | Status |
|---|---|---|
| [001](001-stack-and-ipc.md) | Implementation stack & IPC model | **Amended** — Avalonia→WPF, in-process (not API-owned) hardware, MVC/HTTP not Minimal-API/HTTPS, no service/auth. See its "Superseded" note. |
| [002](002-local-api-surface.md) | Local API surface (loopback HTTP) | Accepted — the shipped optional Server tier. |
