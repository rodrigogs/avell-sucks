# Design

Visual system for Gaming Center — a cyberpunk performance instrument (WPF / .NET 10, dark only). Neon energy on a deep violet-black surface, disciplined so glow marks live data and active state, never chrome.

## Theme

Dark only. Deep violet-black base (not pure black, not navy) with a magenta→cyan neon accent axis. Surfaces are near-flat with subtle depth from tinted elevation + hairline borders; energy comes from accent glow on live/active elements, not from gradients on every panel.

Color strategy: **Committed** — the violet-black base carries the whole surface; the magenta/cyan axis is reserved for live values, active modes, primary actions, and state.

## Color

OKLCH values are the source of truth; hex in parentheses for WPF `Color` literals.

### Base / surfaces
- `--bg`        oklch(0.17 0.03 300)  (#141018) — app background, violet-black
- `--surface`  oklch(0.21 0.035 300) (#1c1622) — panels / cards
- `--surface-2` oklch(0.25 0.04 300) (#251d2e) — raised (headers, selected rows)
- `--overlay`  oklch(0.28 0.045 300) (#2e2438) — popovers, menus, dialogs
- `--hairline` oklch(0.32 0.04 300 / 0.5) (#3a2f47 @50%) — 1px borders/dividers

### Ink (text)
- `--ink`      oklch(0.96 0.01 300) (#f2eef6) — primary text / headings
- `--ink-2`    oklch(0.80 0.02 300) (#c3baccd) → use #c1b6cf — secondary labels (≥4.5:1 on surface)
- `--ink-3`    oklch(0.66 0.02 300) (#948aa3) — muted / hints (large text or non-essential only)
- `--ink-num`  same as --ink but rendered in mono for numerics

### Neon accent axis
- `--magenta`  oklch(0.68 0.26 350) (#ff2e88) — primary accent, active RGB/energy
- `--cyan`     oklch(0.80 0.15 200) (#22d3ee) — live telemetry, secondary accent
- `--violet`   oklch(0.62 0.22 300) (#a855f7) — tertiary, blends the axis
- Accent gradient (reserved, used sparingly): magenta→violet→cyan, 135°.
- Glow = accent color at 0.35–0.6 alpha as a blurred drop-shadow behind the element. Never a fill.

### Semantic state (never hue-alone; pair with icon/label)
- `--ok`       oklch(0.80 0.18 150) (#34e5a0) — verified / applied / safe
- `--warn`     oklch(0.82 0.16 85)  (#f4c04a) — caution / speculative register
- `--danger`   oklch(0.63 0.24 25)  (#f5484a) — blocked / failed / EC-write risk
- `--live`     = --cyan — streaming/telemetry pulse
- Temperature ramp (gauges): cool #22d3ee → #34e5a0 → #f4c04a → #ff2e88 → hot #f5484a

### Contrast rules
- Body/labels: --ink or --ink-2 only on --surface/--surface-2 (both ≥4.5:1). --ink-3 only for ≥18px or decorative.
- Neon on dark passes for large numerics/icons (≥3:1); never neon body text on neon.
- Active-mode chips: neon text on a neon-tinted (8–12% alpha) fill, not neon-on-neon.

## Typography

One family for UI + one mono for numerics/data. No display font (anti-ref: angular "tech" fonts).

- **UI**: Inter (or Segoe UI Variable fallback on Windows) — headings, labels, buttons, body. Weights 400/500/600/700.
- **Mono**: JetBrains Mono (fallback Cascadia Code / Consolas) — all live numerics, RPM, watts, temps, hex colors, addresses. This is where the "instrument" reads.
- Fixed rem/px scale (product register, consistent DPI), ratio ~1.2:
  - display (gauge value) 34 / h1 24 / h2 19 / h3 16 / body 14 / label 12.5 / caption 11
- Letter-spacing: tight on headings (-0.01em), +0.04em on the few small-caps section labels (used as real structure, not an eyebrow on every panel).
- `text-wrap: balance` equivalent — keep headings short; numerics never wrap.

## Components

Consistent vocabulary across all four tabs. Every interactive control has default / hover / focus / active / disabled / loading states.

- **App shell**: left vertical nav rail (icon + label: Dashboard / Fan / RGB / Power), 64–200px; top bar with app mark + live gateway status dot + connection state. Content area = the active tab.
- **Panel**: --surface, 12px radius, 1px --hairline, generous padding. NOT a card grid — panels are sized to their content and role. Nested panels forbidden.
- **Gauge (radial)**: the signature element. Arc track + neon value arc on the temperature ramp, large mono value in center, unit + label below. Sweep animates on value change; glow intensity tracks how hot/loaded.
- **Sparkline / line chart**: live temp & RPM history, cyan stroke, soft area fade, no gridline clutter. Streams from SSE.
- **Mode selector**: segmented control (auto / boost / custom / L1–L5). Active segment = neon-tinted fill + neon label + glow. Inactive = --ink-2 on --surface-2.
- **Curve editor** (fan): draggable temp→PWM points on a plotted curve, neon nodes, live line.
- **Slider** (PL1/PL2/Tau, brightness): track + neon fill + mono value readout; danger-tinted zone past safe range.
- **RGB controls**: HSV color wheel/field + hex mono input, effect picker (static/breathing/cycle/wave/ripple) with animated mini-previews, brightness/speed/direction, a keyboard zone preview that shows the actual effect.
- **State badge**: pill with icon + label + color for write results (allowed/executed/verified/blocked). Never color-only.
- **Buttons**: primary = neon magenta fill (or gradient for the single hero action), text on it ≥4.5:1; secondary = --hairline border + --ink; danger = --danger. All with hover lift + focus ring (2px cyan).
- **Toast**: bottom, --overlay, for apply results and errors.
- Loading = skeleton shimmer on panels; never a centered spinner over content.
- Empty/disconnected = teach the state ("gateway offline — telemetry paused", retry affordance), not blank.

## Layout

- Nav rail left, content right. Rail collapses to icons under ~900px width; the app is resizable (MinWidth 700, from the existing window fix).
- Content uses flex/stack for 1D rows, grid only for the true 2D gauge cluster. `repeat(auto-fit, minmax(...))` analog for the dashboard gauges so they reflow.
- Vary spacing for rhythm: 4 / 8 / 12 / 16 / 24 / 32 scale.
- Semantic elevation, not z-index soup: base → nav → sticky top bar → dialog backdrop → dialog → toast → tooltip.

## Motion

Product motion: 150–250ms, conveys state not decoration.

- Gauge value → arc sweep (ease-out-quart, ~220ms).
- Mode/tab change → crossfade + active-glow transition (~180ms).
- Live telemetry → smooth value interpolation, cyan pulse on the live dot (~1.5s loop).
- Apply/write → the state badge animates in; success glows --ok briefly, blocked shakes subtly + --danger.
- RGB effect previews loop to show the real effect.
- **Reduced motion**: sweeps become instant value sets, pulses become static fills, previews freeze on a representative frame. Every animation needs this path (WPF: gate storyboards on SystemParameters or a setting).

## WPF implementation notes

- Central `Theme.xaml` ResourceDictionary: all colors as `SolidColorBrush`, the gradient as `LinearGradientBrush`, type as named styles, control templates for the shared components. Merge in App.xaml.
- Gauges/charts: custom controls or a lightweight lib (LiveCharts2 / SkiaSharp) — prefer owner-drawn for the radial gauge to control the neon exactly.
- Glow = `DropShadowEffect` (BlurRadius, accent Color, low ShadowDepth) — use sparingly (perf); prefer on a few hero elements, not every control.
- Keep the window-position fix (Manual + WorkArea) — the shell must always render on-screen.

---

# Reactive Architecture Spec

Status: authoritative. Drives implementation. Supersedes ad-hoc per-view state. (This
section is the software architecture; the sections above are the visual system — both apply.)

## 0. Headline decision

Adopt **one standardized reactive stack**:

> **CommunityToolkit.Mvvm `ObservableObject` view-models, bound to a single
> `IHardwareStateService` (the one source of truth), which is fed by three inputs —
> (a) the existing `SensorPump` telemetry tick, (b) an event-driven external-change
> signal (`SystemEvents.PowerModeChanged` now + a WMI `AcpiTest_EventULong`
> `ManagementEventWatcher` later), and (c) a 1–2 s polling reconciler that re-reads
> EC/PL and diffs — plus debounced write-through that reports outcome through the
> existing `WriteState`/`Toaster` contract.**

Winners we pick and do **not** reinvent:

| Concern | Winner | Why |
|---|---|---|
| VM change notification | `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`) | Source-generated `INotifyPropertyChanged`, zero-ceremony, idiomatic WPF; no `ReactiveUI`/`System.Reactive` weight. |
| Composition | `Microsoft.Extensions.DependencyInjection` in `App.OnStartup` | Already referenced transitively (Server uses it); one place to pick stub vs Core impl. |
| Single source of truth | one `IHardwareStateService` exposing observable state stores | Kills state duplication across stubs / controls / flags. |
| Telemetry | keep `SensorPump` (1 Hz `DispatcherTimer`, all-nullable `Telemetry`) | Already single-owner, dispatcher-affine, honest about "n/a". |
| External power-source change | `Microsoft.Win32.SystemEvents.PowerModeChanged` | Standard, no OEM dependency; AC/DC changes PL interpretation. |
| External mode/fan change | `ManagementEventWatcher` on `AcpiTest_EventULong` + polling reconciler | Only push signal that fires even when the OLD app is closed. |
| Write contract | existing `ControlResult`/`WriteState` (`Allowed/Executed/Verified/Error`) | 1:1 shape match to Core `EcWriteResult` and the API DTOs. Keep. |
| Write feedback | existing `Toaster`/`ToastHost` | Already the standardized outcome vocabulary. |
| Write coalescing | existing `Debouncer` (450 ms) | Already the live-apply-on-settle primitive. |
| Motion gating | existing `MotionPrefs` | Keep as the switch the animation layer respects. |

## 1. The ONE reactive state model

Exactly one owner of domain state: **`IHardwareStateService`** (singleton). It exposes
per-domain `ObservableObject` **state stores** the UI binds to. Controls and code-behind
flags stop being the truth.

```
                    ┌───────────────────── IHardwareStateService (singleton) ─────────────────────┐
 (a) SensorPump ───▶│  TelemetryStore : ObservableObject (CpuTempC, GpuLoad … all nullable)       │
     1 Hz tick      │  FanStore       : ObservableObject (Mode, Curve[5], WritesEnabled, …)        │
                    │  PowerStore     : ObservableObject (Mode, Pl1/Pl2/Pl4, AcOnline, Supported)  │
 (b) SystemEvents  ▶│  RgbStore       : ObservableObject (Available, Effect, Color, Brightness)    │
   + WMI watcher    │                                                                              │
 (c) Reconciler   ─▶│  Writes go THROUGH the service so it can re-read + reconcile and set an      │
   poll + diff      │  ExternalChange flag when the diff wasn't ours.                              │
                    └──────────────────────────────────────────────────────────────────────────────┘
                          ▲ bind (OneWay reads, TwoWay editable)   │ [RelayCommand] / debounced setters
              FanViewModel / PowerViewModel / RgbViewModel / DashboardViewModel  (ObservableObject)
                          ▲ Binding — views hold NO state in code-behind
                    Fan / Power / Rgb / Dashboard views (XAML)
```

**(a) Live telemetry pump.** `SensorPump.Tick (Action<Telemetry?>)` is subscribed **once** by
the state service and projected onto `TelemetryStore`. Preserve all-nullable semantics:
`null` → VM formats "n/a", never `0`. Stays on the dispatcher (pump uses `DispatcherTimer`).
Replaces every `_pump.Tick += OnTelemetry; CpuGauge.Load = …` in the views.

**(b) Event-driven external notifications (low-latency triggers).**
- `SystemEvents.PowerModeChanged` → on `PowerModes.StatusChange`, read
  `SystemInformation.PowerStatus.PowerLineStatus`, update `PowerStore.AcOnline`, kick the
  reconciler (PL interpretation is AC/DC-dependent). Debounce — StatusChange also fires on
  battery-percentage ticks.
- (Phase 3) `ManagementEventWatcher(new WqlEventQuery("SELECT * FROM AcpiTest_EventULong"))`
  in `root\WMI`. On any scancode — especially **176 (0xB0) QKey fan-mode switch** and
  **167 (0xA7) fan-boost** — trigger an immediate EC re-read. The event says "something
  changed", not the new value, so it always chains into a read.

**(c) Polling reconciler (authoritative reconciliation).** A `DispatcherTimer` (1–2 s, matching
the OLD app's cadence) re-reads EC/PL truth (fan-control byte `0x751`, capability `0x782`,
PL `0x783/0x784/0x785`), **diffs against the store**, and:
- new value ≠ store value **and** no local write in flight → update store **and** set
  `LastChangeWasExternal = true` (subtle "changed on device" cue, not a write toast);
- local write in flight → the reconciler is the read-back verification, confirming `Verified`.

**How a change made in the OLD Gaming Center surfaces in ours:**
1. User picks a mode (or presses the Fn fan key) in the OLD app / hardware.
2. EC RAM bytes change; BIOS raises WMI `AcpiTest_EventULong` (0xB0/0xA7); if the OLD app runs,
   `HKLM\SOFTWARE\OEM\GamingCenter\Monitor\FanMode` updates.
3. Our `ManagementEventWatcher` fires → immediate EC re-read (push). Even with the OLD app closed
   and no registry write, the WMI event + the 1–2 s reconciler catch it.
4. Read model returns new bytes; the service diffs, updates `FanStore`/`PowerStore`, flags external.
5. `INotifyPropertyChanged` fires → bound cards/RadioButtons re-render. The UI **mirrors** the
   device; it never assumed its own last write was still true.

We deliberately do **not** read Windows power plans / overlay GUIDs — RE proved the OEM "power
mode" lives in EC PL bytes, not powrprof.

## 2. Live-apply interaction standard

**Removing Apply buttons is correct.** Every control actuates on change; there is no "pending
edit" state to reconcile against the device because the reconciler keeps the mirror truthful.
Already implemented on stubs in Fan/Power/Rgb — ratified as the standard.

- **Debounce window:** `450 ms` trailing, via existing `Debouncer`. Discrete choices (mode
  RadioButtons) fire immediately; continuous edits (PL sliders, curve drag, RGB color/brightness)
  debounce. A discrete press **cancels** a pending continuous write (`_curveWrite.Cancel()` /
  `_limitWrite.Cancel()`) — keep this rule.
- **Optimistic vs confirmed:** **optimistic echo, confirmed truth.** The edited control reflects
  input immediately; a `Pending` toast shows during the async write; the store updates to the
  *device-confirmed* value on read-back (`Verified`). If read-back disagrees or the write is
  `Blocked`/`Failed`, the store re-asserts the real value and the bound control snaps back.
- **Feedback:** `WriteState` → `Toaster` is the standard — `Pending` (spinner, persists) →
  `Verified` (auto-dismiss 3.2 s) / `Failed` (reason, persists) / `Blocked` (writes-off, persists).
  One toast at a time; color always paired with icon+label; gate animation on `MotionPrefs`.
- **Escape hatches (the only explicit actions that remain):**
  1. **Reset to Auto** (Fan) — hands the fan back to firmware; already present.
  2. **Write gate** — `GAMINGCENTER_ALLOW_EC_WRITES != 1` → writes report `Blocked`; per-view
     `GateNotice` explains it. No silent failure.
  3. **Advanced disclosure** (Power) — raw PL sliders stay behind a toggle; mode cards are default.

## 3. Mapping onto the existing seams

| Seam | Today | Change |
|---|---|---|
| **#1 Composition root** | `App.OnStartup` → `new MainWindow().Show()`; views field-init `new LocalXService()`; `MainWindow` news up views + one `SensorPump`. | Add MS.DI in `App.OnStartup`: register `SensorPump`, `IHardwareStateService`, the four VMs, and `IFanService/IPowerService/IRgbService` (stub **or** Core-backed, chosen once). Resolve `MainWindow`; inject VMs, set as each view's `DataContext`. Delete the four `new LocalXService()` field initializers. |
| **#2 Write contract** | `ControlResult` + `WriteState`; views call `await _service.SetXAsync` then `Toaster.Show`. | **Keep verbatim.** Move the write into `[RelayCommand]`/debounced VM setters. When Core lands, `EcWriteResult` (identical shape) maps 1:1 to `ControlResult`; `FanController`/`PowerController` DTOs already aggregate this way. |
| **#3 Telemetry** | `SensorPump.Tick` subscribed per-view in `Loaded`, poking controls. | Subscribe **once** in `IHardwareStateService`; project onto `TelemetryStore`. Dashboard/Fan VMs bind. Keep `SensorPump` UI-thread; keep Dashboard's disk/network Nth-tick sampling behind the store. |
| **#4 Single source of truth** | State split across stub fields, WPF control props, `_loading/_initialized/_advancedDirty` flags. | All authoritative state → the stores. The `InitializeComponent`-ordering guards (`OnLimitChanged`, `OnEffectChecked` firing before siblings exist) disappear: XAML binding has a real load/bind lifecycle. VMs keep an `IsLoading` guard only where TwoWay bindings would echo during initial hydration. |
| **Real backend wiring** | `WmiEcBackend`/`SafeEcWriter`/`WriteGate`/`EcWriteAllowlist` wired ONLY in `Server/Program.cs`; UI never touches it. | Register a Core-backed `IFanService`/`IPowerService` in the composition root wrapping `IEcBackend.ReadSnapshotAsync`/`InterpretFanModeAsync`/`ReadPowerProfileAsync` (reads) and `SafeEcWriter.WriteAsync` (writes). **In-proc is the choice** — the UI manifest is already `requireAdministrator` (for LibreHardwareMonitor ring-0), and `WmiEcBackend` needs the same elevation, so no separate elevated Server is required. The loopback Server (Api FanController/PowerController/EventsController) stays a valid alternate transport, not the primary path. |
| **Elevation** | UI manifest `requireAdministrator`. | Satisfies WMI `AcpiTest_MULong.GetSetULong` and `WriteGate`'s env check (`GAMINGCENTER_ALLOW_EC_WRITES`, the same flag `WriteGateInfo` reads). |
| **RGB** | `LocalRgbService.Available=false`; only `IRgbBackend` interface exists in Core. | Stays `Available=false`/`Blocked` until a HID `IRgbBackend` is written. VM/store/binding work is identical; only the backend is deferred. |
| **Statics → services** | `Toaster`, `MotionPrefs` are static hubs (their comments cite "no DI container"). | Keep as-is through Phase 1–2 (they work without DI). Optionally promote to injected services once the container exists; not required for correctness. |

## 4. Phased implementation plan (each phase independently shippable)

### Phase 1 — Live-apply UI on stubs *(underway)*
- Toast + `WriteState` feedback and debounced live-apply already exist in Fan/Power/Rgb.
- **Standardize it:** add `CommunityToolkit.Mvvm`; introduce the composition root (MS.DI in
  `App.OnStartup`); create `Fan/Power/Rgb/DashboardViewModel` (`ObservableObject`) and
  `IHardwareStateService` with in-memory stores fed by the stub services; move state off
  controls/flags into stores; convert views to XAML bindings. Backend stays stub.
- **Verify:** app runs; every control actuates through a VM command; toasts show correct
  `Pending→Verified/Blocked`; gate notices intact; state survives tab switches (store persists,
  no re-read-on-Loaded needed).

### Phase 2 — Real read model + polling reconciler
- Register Core-backed `IFanService`/`IPowerService` (in-proc `WmiEcBackend` + `SafeEcWriter`)
  behind the same interfaces; select via composition root (env/flag: stub vs real).
- Implement the **read model**: decode fan-control byte `0x751` (`MyFanCTLByteFlag`), capability
  `0x782`, PL `0x783/0x784/0x785`, using the proven `addr + 0x10000000000` read encoding
  (`ec-read-probe.json`). Feed the stores.
- Add the **1–2 s polling reconciler** in `IHardwareStateService`: re-read, diff, update store,
  set `LastChangeWasExternal`; this is also the write read-back/verification path (`Verified`).
- Map `EcWriteResult` → `ControlResult` in the Core-backed service.
- **Verify:** with `GAMINGCENTER_ALLOW_EC_WRITES=1` on real hardware, a mode change reads back
  `Verified`; polling keeps values live; writes-off still reports `Blocked`; compare addresses
  against `ec-read-probe.json`.

### Phase 3 — External-change reflection (push hooks)
- Wire `SystemEvents.PowerModeChanged` → update `PowerStore.AcOnline` + kick reconciler (debounced).
- Wire `ManagementEventWatcher` on `AcpiTest_EventULong` (root\WMI); scancodes 176/167 (and
  172/173/174 thermal/battery) → immediate EC re-read.
- Optional: `RegNotifyChangeKeyValue`/`RegistryValueChangeEvent` watch on
  `HKLM\SOFTWARE\OEM\GamingCenter\Monitor\FanMode` and `MyFan3\*` as a fast-path when the OLD app runs.
- Surface `LastChangeWasExternal` as a subtle "changed on device" cue (not a write toast).
- **Verify:** press the physical fan key or change mode in the OLD app → our UI updates within one
  reconcile cycle (near-instant via the WMI event), showing the external cue rather than a
  self-authored toast.

## 5. Non-negotiables (preserve)

- `Telemetry` stays **all-nullable**; UI shows "n/a", never a fake `0`.
- `SensorPump` stays single-owner, dispatcher-affine, lazy-open on `Start()`.
- `ControlResult`/`WriteState` vocabulary is frozen (bridges stub ↔ Core ↔ API).
- `Debouncer` (450 ms) is the only write-coalescing primitive.
- All animation gates on `MotionPrefs.ReducedMotion`.
- The EC — not Windows power plans — is the source of truth for fan/power mode.
