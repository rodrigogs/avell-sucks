# Visual system

Visual system for AvellSucks — a cyberpunk performance instrument (WPF / .NET 10, dark only). Neon energy on a deep violet-black surface, disciplined so glow marks live data and active state, never chrome.

> The software architecture that used to sit at the bottom of this file now lives in [`../ARCHITECTURE.md`](../ARCHITECTURE.md).

## Theme

Dark only. Deep violet-black base (not pure black, not navy) with a magenta→cyan neon accent axis. Surfaces are near-flat with subtle depth from tinted elevation + hairline borders; energy comes from accent glow on live/active elements, not from gradients on every panel.

Color strategy: **Committed** — the violet-black base carries the whole surface; the magenta/cyan axis is reserved for live values, active modes, primary actions, and state.

## Color

OKLCH values are the source of truth; hex in parentheses for WPF `Color` literals. (These match `app/src/AvellSucks.UI/Theme/Palette.xaml`.)

### Base / surfaces
- `--bg`        oklch(0.17 0.03 300)  (#141018) — app background, violet-black
- `--surface`  oklch(0.21 0.035 300) (#1c1622) — panels / cards
- `--surface-2` oklch(0.25 0.04 300) (#251d2e) — raised (headers, selected rows)
- `--overlay`  oklch(0.28 0.045 300) (#2e2438) — popovers, menus, dialogs
- `--hairline` oklch(0.32 0.04 300 / 0.5) (#3a2f47 @50%) — 1px borders/dividers

### Ink (text)
- `--ink`      oklch(0.96 0.01 300) (#f2eef6) — primary text / headings
- `--ink-2`    oklch(0.80 0.02 300) (#c1b6cf) — secondary labels (≥4.5:1 on surface)
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
- `--warn`     oklch(0.82 0.16 85)  (#f4c04a) — caution / gated-off write
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
- Keep headings short; numerics never wrap.

## Components

Consistent vocabulary across all tabs. Every interactive control has default / hover / focus / active / disabled / loading states.

- **App shell**: left vertical nav rail (icon + label). Primary items **Dashboard / Fan / RGB / Performance**; **Settings** and **About** pinned at the rail footer. Content area = the active tab.
- **Panel**: --surface, 12px radius, 1px --hairline, generous padding. NOT a card grid — panels are sized to their content and role. Nested panels forbidden.
- **Gauge (radial)**: the signature element. Arc track + neon value arc on the temperature ramp, large mono value in center, unit + label below. Sweep animates on value change; glow intensity tracks how hot/loaded.
- **Sparkline / line chart**: live temp & RPM history, cyan stroke, soft area fade, no gridline clutter.
- **Mode selector**: segmented control (auto / boost / custom / L1–L5). Active segment = neon-tinted fill + neon label + glow. Inactive = --ink-2 on --surface-2.
- **Curve editor** (fan): draggable temp→PWM points on a plotted curve, neon nodes, live line.
- **Slider** (PL1 / PL2 / PL4, brightness): track + neon fill + mono value readout; danger-tinted zone past safe range. (Power sliders live behind an Advanced disclosure; ranges PL1 15–90, PL2 15–120, PL4 15–140 W.)
- **RGB controls**: HSV color wheel/field + hex mono input, effect picker (static/breathing/cycle/wave/ripple) with animated mini-previews, brightness/speed/direction, a keyboard zone preview. (RGB backend is a stub — see the README's keyboard note.)
- **Toggle switch**: pill track + sliding knob for settings (off = hairline, on = magenta + glow).
- **Buttons**: primary = neon magenta fill (or gradient for the single hero action), text on it ≥4.5:1; secondary = --hairline border + --ink; danger = --danger. All with hover lift + focus ring (2px cyan).
- **Toast**: top-center, --overlay, for apply results and errors (the write-outcome vocabulary; never color-only).
- Loading = skeleton shimmer on panels; never a centered spinner over content.
- Empty/disconnected = teach the state, not blank.

## Layout

- Nav rail left, content right. The app is resizable (the window-position fix keeps it on-screen).
- Content uses flex/stack for 1D rows, grid only for the true 2D gauge cluster, reflowing the dashboard gauges.
- Vary spacing for rhythm: 4 / 8 / 12 / 16 / 24 / 32 scale.
- Semantic elevation, not z-index soup: base → nav → sticky top bar → dialog backdrop → dialog → toast → tooltip.

## Motion

Product motion: 150–250ms, conveys state not decoration.

- Gauge value → arc sweep (ease-out-quart, ~220ms).
- Mode/tab change → crossfade + active-glow transition (~180ms).
- Live telemetry → smooth value interpolation, cyan pulse on the live dot (~1.5s loop).
- Apply/write → the toast animates in; success glows --ok briefly, failure --danger.
- **Reduced motion**: sweeps become instant value sets, pulses become static fills, previews freeze. Every animation needs this path (gated on `MotionPrefs`).

## WPF implementation notes

- Theme lives in `Theme/Palette.xaml` + `Typography.xaml` + `Controls.xaml`, merged in `App.xaml`: all colors as `SolidColorBrush`, type as named styles, control templates for the shared components.
- Gauges/charts: owner-drawn custom controls (e.g. `LoadTempGauge`, `FanCurveEditor`, `CapacityBar`) to control the neon exactly; shared brand colors via `Brand`.
- Glow = `DropShadowEffect` (BlurRadius, accent Color, low ShadowDepth) — use sparingly (perf); a few hero elements, not every control.
- Keep the window-position fix (Manual + WorkArea) — the shell must always render on-screen.
