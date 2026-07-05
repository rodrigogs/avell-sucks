# ADR-001: Implementation Stack and IPC Model

Status: Accepted  
Date: 2026-07-03  
Decider: Planner

## Context
We need an MVP app to control gaming hardware on a Windows laptop: EC fan/RGB, lightbar, OEM service CLI, ITE HID RGB KB. The implementation must start from an existing reverse-engineering base and allow a later Linux portability split, because the EC/HID/oem-service layer is Windows-specific.

We evaluated three candidate architectures.
1. .NET 10 + Avalonia UI + Windows helper/service + ASP.NET Core local API
2. Tauri 2 + Rust core + web UI
3. Fallback CLI daemon + separate GUI

## Option Evaluation

### A: .NET 10 + Avalonia + Windows service/helper + local ASP.NET Core API

Strengths
- First-class WMI and HID interop in .NET; matches the existing reverse-engineered protocol surface directly.
- ASP.NET Core can run as a Windows service and also exposes an optional local HTTP API with low ceremony.
- Avalonia is genuinely cross-platform, so at least the shell can later run on Linux.
- Rich ecosystem for DI, typed IPC, logging, and packaging.

Weaknesses
- Avalonia packaging/runtime is heavier than Tauri.
- Windows service elevation, IPC authentication, and transport security around loopback HTTP need extra attention.
- Linux limit: Avalonia cross-platform UI is possible, but the hardware backend must stay Windows-specific.

Fit: High for hardware I/O; medium for future Linux UI migration.

### B: Tauri 2 + Rust core + web UI

Strengths
- Small Windows footprint; Rust ecosystem for HID is strong (`hidapi`, `rust-hidapi`).
- Tauri sidecar/process model cleanly isolates privileged backend from unprivileged frontend.

Weaknesses
- No mature WMI-first model. EC WMI reads/writes via `AcpiTest_MULong` require COM/WMI interop that is awkward in Rust and much harder to validate against the existing protocol map.
- OEM service CLI invocation, OLED paths, and ITE HID feature-report protocols need nontrivial Rust/Win32 FFI work.
- Less developer leverage against already-known C#-style call sites and encoding behavior.

Fit: Lower fit for Windows-only hardware; better later if Linux hardware access matures.

### C: CLI daemon + separate GUI

Strengths
- Simple separation of concerns; daemon can be restarted independently.

Weaknesses
- IPC becomes a custom protocol/CLI parsing problem instead of a typed API boundary.
- Packaging and discovery UX is weakest.
- No production interop framework out of the box.

Fit: Only as fallback if UI framework constraints change later.

## Decision

Accept Option A for MVP.

MVP architecture:
- Frontend: Avalonia UI on .NET 10.
- Local API: ASP.NET Core Minimal API over HTTPS on localhost, optionally hosted inside a Windows service.
- IPC surface: localhost HTTPS JSON API with token or Windows-integrated auth; one local API process owns all hardware access.
- Hardware backend: Windows-only class library layer implementing EC WMI, ITE HID, OEM CLI orchestration, and later lightbar/OLED.
- Linux boundary: UI shell can be abstracted behind Avalonia for future Linux port, but hardware backend stays Windows-only for MVP and likely indefinitely for EC/HID/oem-service.

Why not Tauri:
The primary failure mode is WMI-centered EC access and existing protocol documentation. A .NET backend matches the known interfaces most naturally, lowers implementation risk, and keeps future Avalonia cross-platform UI reuse instead of requiring a Rust-side service rewrite.

## Consequences

Short term
- One repo with shared hardware contracts and one Avalonia app.
- Local API backends: `127.0.0.1:5000+` HTTPS.
- All hardware access in a single Windows-only process; GUI never calls WMI/HID directly.

Medium term
- Separate library project for hardware backend; Avalonia shell becomes view-only.
- Linux port continues as Avalonia shell only once/if hardware access has a Linux implementation.

Risk
- Local API token storage and transport security is manual/lightweight at first.
- Elevation should rotate around Windows service/helper for privileged writes rather than running full app elevated.
