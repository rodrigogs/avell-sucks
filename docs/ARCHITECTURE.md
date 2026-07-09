# Architecture (as shipped)

How AvellSucks is actually built — the real, current design. (An earlier
"Reactive Architecture Spec" in `DESIGN.md` proposed a CommunityToolkit.Mvvm + DI
+ store-based stack; that was **not adopted** — the notes below describe what
runs. See [§Not adopted](#not-adopted).)

## Solution layout

`app/AvellSucks.Replacement.slnx` — .NET 10:

| Project | Role |
|---|---|
| `AvellSucks.Core` | Portable contracts + models + the safe-write pipeline (`SafeEcWriter`, `EcWriteAllowlist`, `WriteGate`, `FanModeMap`, `EcPipeline`, `JsonlAuditLog`, `BatchWriteResultDto`). No Windows deps. |
| `AvellSucks.Core.Windows` | `WmiEcBackend` — the WMI ACPI EC read/write backend (Windows-only). |
| `AvellSucks.UI` | The WPF app — the shipped product. Drives hardware **in-process** via Core + Core.Windows. |
| `AvellSucks.Api` | ASP.NET MVC controllers + `SystemSnapshotBuilder` for the optional loopback server. |
| `AvellSucks.Server` | Optional loopback HTTP host (`127.0.0.1:5055`) wiring the Api over Core via DI. |
| `AvellSucks.Core.Tests` / `AvellSucks.Server.Tests` | xUnit suites (safe-write pipeline, allowlist, gate, fan controller, audit log). |

**Two independent front-ends over one Core:** the WPF UI and the loopback Server
are separate consumers of `AvellSucks.Core`. The UI does **not** depend on the Api
project and does not need the Server running — it talks to the EC directly. The
Server is an optional automation surface.

## UI composition (WPF, code-behind)

- **No MVVM framework, no DI container in the UI.** Views are XAML + code-behind.
  `HardwareServices` is a small static composition root that picks the real
  WMI-backed services when writes are enabled and elevated, else in-memory stubs
  (`Local*Service`) — so the app runs anywhere.
- **Services** (`IFanService`, `IPowerService`, `IRgbService`) return a
  `ControlResult` (Allowed/Executed/Verified/Error) that maps 1:1 to Core's
  `EcWriteResult`.
- **Telemetry:** one `SensorPump` (1 Hz) owns a LibreHardwareMonitor monitor,
  opened off-thread and sampled off-thread, marshalling an all-nullable
  `Telemetry` back to the UI thread. `null` renders as "n/a", never a fake `0`.
- **Live-apply, no Apply button:** edits actuate on settle via a 450 ms
  `Debouncer`; write outcomes surface through `Toaster`/`ToastHost` (the
  Pending → Verified/Failed/Blocked vocabulary). `MotionPrefs` gates animation.
- **Reactivity to external change:** per-tab polling reconcilers —
  `FanStateMonitor` and `PowerStateMonitor` (`DispatcherTimer`, suspend/resume
  around our own writes, diff-then-signal `ExternalModeChanged`) — keep the UI in
  sync when the OEM app / Fn key / another tool changes state. The EC (not the
  app's last write) is the source of truth.
- **i18n:** runtime `.resx` via a `Loc` provider + `{loc:Tr}` markup extension;
  `Loc.OnCultureChanged` re-localizes imperatively-set strings. Default follows
  the Windows display language (pt/pt-BR → PT, else EN); persisted in Settings.
- **Paths:** preferences in `%AppData%\AvellSucks\settings.json`; logs + the
  EC-write audit under `%ProgramData%\AvellSucks` (`AppPaths`).

## Hardware write path

The one pipeline both front-ends share, assembled via `EcPipeline.BuildWriter`:

```
UI service / Api controller
      → SafeEcWriter.TryWriteAsync(addr, value)
          gate (GAMINGCENTER_ALLOW_EC_WRITES) → allowlist (EcWriteAllowlist)
          → before-snapshot → WmiEcBackend.WriteAsync → read-back verify (settle+retry)
          → rollback on mismatch → JsonlAuditLog
      → EcWriteResult (never throws)
```

Register facts and the EC WMI protocol are documented in
[`reverse-engineering.md`](reverse-engineering.md).

## Optional loopback server

`AvellSucks.Server` hosts `AvellSucks.Api` on `http://127.0.0.1:5055` (HTTPS
opt-in via `GAMINGCENTER_REQUIRE_HTTPS=1`), loopback-only (non-localhost → 403),
no auth. Endpoints in [`api.md`](api.md). Same Core pipeline, wired with
`Microsoft.Extensions.DependencyInjection` instead of the UI's static root.

## Distribution

Per-machine Inno Setup installer (`installer/AvellSucks.iss`) → `Program Files`,
Start-menu shortcut, uninstaller. In-app updater checks GitHub Releases and
relaunches the installer silently. CI: `.github/workflows/release.yml` on a `v*`
tag. Details in the READMEs.

## Not adopted

The `DESIGN.md` "Reactive Architecture Spec" proposed CommunityToolkit.Mvvm
`ObservableObject` view-models bound to a single `IHardwareStateService`, composed
with `Microsoft.Extensions.DependencyInjection`, plus a WMI `AcpiTest_EventULong`
`ManagementEventWatcher`. **None of that shipped.** The reactivity goal it aimed
at was met more simply by the code-behind views + `FanStateMonitor`/
`PowerStateMonitor` polling reconcilers described above. The idea is kept in the
design history, not as a status tracker.
