# Dashboard Design Spec

_Synthesized from multi-source research (HWiNFO, Task Manager, Afterburner, btop, MS docs, LHM source). Guided the dashboard redesign; the PL/TGP readouts are now backed by real EC registers._

---

# Neon Dashboard — Concrete Design Spec

## 0. Theme tokens + two global rules (everything below references these)

```
--bg-base      #0A0512     --text-hi   #F3ECFF     --brand-magenta #FF2E97
--bg-panel     #150A28     --text-mid  #B6A0E0     --brand-cyan    #22D3EE
--bg-elevated  #1F0F3A     --text-dim  #7C6AA6     --brand-grad    magenta→cyan @135°
--stroke       #3A2160     --track     #241041   (all meter backgrounds)
```
Semantic thermal/severity ramp (the ONLY place green/amber/red is allowed):
```
cold #22D3EE · normal #35F0A0 · warm #FBBF24 · hot #FF8A3D · critical #FF3355
```

**Rule A — two color languages, never mixed in one widget.**
- *Identity color* = which component (CPU = `--brand-magenta`, GPU = `--brand-cyan`). Used for trend lines, reference lines, tile chrome.
- *Severity color* = how bad (the ramp above). Used for temp readouts, capacity-meter overflow, status chips.
Load gauges use the **brand gradient only** and are **never tinted red** — 95–100% load is healthy (GPU-bound), so severity does not apply to load.

**Rule B — capacity meters flip to severity on threshold.** RAM/Commit/VRAM bars fill with `--brand-cyan→magenta` while healthy, and the fill (not the track) recolors amber then red at each domain's threshold (§3). Load gauges are exempt (Rule A).

---

## 1. Temperature thresholds + colors (CPU and GPU differ — laptop-tuned)

CPU is bounded by Tjmax ~100–105 °C; a laptop dGPU (NVIDIA) begins software clock-down at ~87 °C and hardware-slowdown at ~102 °C, so the two bands are intentionally not the same.

**CPU (°C)**
| Band | Range | Color | Meaning |
|---|---|---|---|
| Cold | ≤ 45 | `#22D3EE` | idle |
| Normal | 46–74 | `#35F0A0` | healthy under load |
| Warm | 75–84 | `#FBBF24` | warm for a laptop |
| Hot | 85–94 | `#FF8A3D` | approaching throttle |
| Critical | ≥ 95 | `#FF3355` | sustained-throttle / Tjmax region |

**GPU (°C) — NVIDIA laptop**
| Band | Range | Color | Meaning |
|---|---|---|---|
| Cold | ≤ 45 | `#22D3EE` | idle |
| Normal | 46–79 | `#35F0A0` | reliability sweet spot (<80) |
| Warm | 80–86 | `#FBBF24` | approaching Max-Op-Temp (87) |
| Hot | 87–101 | `#FF8A3D` | software clock-down active |
| Critical | ≥ 102 | `#FF3355` | hardware slowdown (shutdown 105) |

Make bands overridable per-sensor (conservative fixed defaults cry wolf on in-spec Ryzen).

**The hot/cold visual effect — exact convention (pick this one):**
Temperature is a numeric readout (`72°`) whose **fill = its band color**, wrapped in a `DropShadowEffect` (glow) that *blooms with heat*:
- `Color` = band color; `ShadowDepth = 0`; `BlurRadius` linearly interpolated **8 px (cold) → 28 px (critical)**; `Opacity` 0.5 → 0.9 over the same range.
- Result: the number reads tight-and-icy-cyan when cool and swells into a hot red bloom — heat is legible pre-attentively, before you read the digits.
- **Critical band only:** add one slow glow pulse (`DoubleAnimation` on Opacity, 1.1 s, autoreverse) as an alert. No animation in any other band (live data stays static — animations off everywhere else).
- Optional glyph that crossfades at the warm boundary: snowflake (cold/normal) → flame (hot/critical), tinted to the band.

---

## 2. Per-component layout — CPU tile and GPU tile (identical structure)

One tile per component. The hero is a single compound object that carries load **and** temp with zero redundancy:

- **Hero radial gauge — arc = LOAD %.** 270° sweep, hard 0–100 scale, fill = `--brand-grad`, track `--track`. Aggregate load only.
- **Gauge hollow center = the temperature readout** (§1 effect). Arc encodes load, core encodes temp — two facts, one focal object, no repeated numbers.
- **Stat strip beneath the gauge, 3 cells, all numeric (no gauges, no bars except the power PL bar):**

| Cell | Value | How | Notes |
|---|---|---|---|
| Clock | **Effective clock, MHz** | number + unit | Effective (not reported boost) is the honest value; label it "eff". Reported/boost lives in Tier 2. |
| Power | **W** + slim PL bar | number, with a thin horizontal bar = draw ÷ power-limit | The *ratio to TDP/TGP* is the signal (near limit = healthy; far below + low FPS = starved). Seed defaults per SKU. |
| Status | **derived chip** | one pill, severity-colored | See derived-state table. |

**Derived status chip (this is what makes it a dashboard, not a number wall):**
| Condition | Chip | Color |
|---|---|---|
| GPU load 95–100% | GPU-bound (healthy) | cyan/normal |
| GPU 50–90% AND any CPU core ~100% | CPU-bound | warm |
| Clock ↓ AND temp in Hot/Critical | Thermal throttle | critical |
| Clock ↓ AND temp normal AND power ≈ limit | Power-limit throttle | warm |
| otherwise | Nominal | dim |

**No redundancy:** load appears only as the arc; temp only as the core (+ trajectory in §4); clock one value; power one value+bar. Everything else is on-demand:

**Tier 2 (expander) per tile:** per-core load (mini bar row / heatmap), per-core temps, Vcore/voltages, CPU CCD temps (multi-chiplet Ryzen only), **GPU Hot-Spot/Junction** as a *labeled secondary* value (never a second "GPU temp" tile — it runs 10–20° hotter and alarms if unlabeled), reported-vs-effective clock, GPU memory clock, and Current/Min/Max/Avg with a reset button.

---

## 3. Memory — three parallel cards, three address spaces, never summed

The #1 redundancy killer: Physical / Commit(swap) / VRAM are independent domains — one bar + one GB/GB headline + one % each, never used/free/percent as three tiles. LHM trap: physical and virtual nodes use identical sensor names — disambiguate by parent node.

**Card 1 — Physical RAM** (LHM `Total Memory → Memory Used / Available / Memory%`)
- One segmented bar: **`In use` | `Cached (Standby)` | `Free`**.
- `In use` = `--brand-cyan→magenta`; **`Cached` = muted `#6D4AA0` (desaturated, distinct)** — it is reclaimable, so never colored like used and never like free; `Free` = `--track`.
- Headline = **`Used / Total GB` + %** (e.g., `11.8 / 16.0 GB · 77%`). Never headline "free" (empty RAM is wasted RAM).
- Fill flips amber ≥ 80%, red ≥ 95% (Rule B).

**Card 2 — Commit / Pagefile (this is your "swap")** (LHM `Virtual Memory` node)
- This is the real out-of-memory predictor and gets **equal visual weight to RAM**, not a footnote. "Swap in use" alone is meaningless; what matters is **commit charge ÷ commit limit** (limit = RAM + all pagefiles).
- One bar `charge / limit` + %. Thresholds: **< 85% brand · 85–95% amber · > 95% red**; draw a faint tick at **90%** (Windows auto-grows the pagefile there).
- Teaching case the layout must make obvious: *60% RAM + 95% commit* is the box about to crash, not *90% RAM + 50% commit*.

**Card 3 — VRAM** (NVIDIA GPU node)
- Headline bar = **Dedicated used / total**, using **per-process `D3D Dedicated Memory Used`** as the primary number (adapter-wide `GPU Memory Used` over-reports by GBs — show it only as a faint secondary labeled "allocated" so the two never look contradictory).
- Append a **thin `Shared` segment that is invisible at 0 and lights amber→red on any spillover** — shared > 0 during a game = VRAM-spillover / frame-drop warning, the single most useful gaming signal here.
- **Drop "Total GPU memory"** (dedicated + shared) entirely — it's the misleading number.
- Dedicated fill: brand < 80%, amber 80–95%, red ≥ 95%.

All three cards visually parallel; VRAM keeps MB precision available (LHM GPU sensors are MB) but shows GB to 1 decimal.

---

## 4. Trend chart — temperature trajectory only

The gauges already show instantaneous load/temp; the chart's only non-redundant job is the **time dimension of temperature** (thermal inertia — is it climbing toward throttle?). So:

- **Plot: CPU temp + GPU temp. Two lines. Nothing else.** No load, no clock, no RPM, no watts — those are bounded/different-unit and belong in tiles.
- **Overlay both on one shared fixed axis** (2 same-unit series, and you want "which is hotter right now" — overlay is the only layout that answers that; under the ≤4–5-line rule it stays clean). If you ever toggle a 3rd series, switch to stacked small-multiples with the same shared axis.
- **Y-axis: fixed 30–100 °C. No zero baseline** (a 0-based axis squashes the 40→85 swing into the top half and destroys the trend). Optional *soft*-max only; never hard auto-scale (flat idle would look like a storm).
- **Series color = identity: CPU `#FF2E97`, GPU `#22D3EE`** (Rule A). Severity is not encoded on the lines — it's carried by faint background zones + reference lines:
  - Zone fills at ~8% opacity: **80–90 amber, 90–100 red.**
  - Two dashed reference lines colored to their series: **GPU 87 °C** (cyan, "clock-down") and **CPU 95 °C** (magenta, "Tjmax-warn").
- **Window: 60 s default**, presets 60 s / 5 min / 15 min (no free zoom).
- **Sample + redraw at 1 Hz**, on a `DispatcherTimer` — never redraw on the sensor callback thread.
- **Interpolation: linear. Markers: none. Gaps: shown, not bridged** (a dropped sensor must not draw as a flat valid line). No spline (invents/rounds spikes), no series animation/tweening.
- **Library: ScottPlot.WPF**, `DataStreamer(60)` fixed rolling buffer, `Signal`/`SignalXY` (not `Scatter`), pre-allocated arrays mutated in place. At 60 s/1 Hz (~60 pts) no downsampling. Only for the 15 min window: decimate with **LTTB or min/max-per-bucket — never plain average** (averaging erases the throttle spike you care about).

---

## 5. Fan section — RPM + active mode

- **Mode selector (top, is both status and control):** segmented pill — `Quiet · Balanced · Performance · Custom`. Active segment lit with `--brand-grad`; others `--text-dim`. This is the universal top-level control, so it sits at the top, not buried.
- **Two fan chips** (laptops have both): **CPU Fan** and **GPU Fan**, each = **RPM number + a thin arc/bar = % of that fan's max RPM** (needs per-fan max to compute %). Optional fan glyph rotating at a rate proportional to RPM (capped; purely an affordance — keep it cheap).
- **Derived flag:** fan ≥ ~95% of max AND temp in Hot/Critical → "Fans maxed" indicator (pairs with the tile's Thermal-throttle chip).
- **Tier 2 (control):** fan-curve editor, X = temp / Y = fan %, with the hard **monotonic non-decreasing constraint** enforced in the editor. NOTE: what shipped is a **5-point curve (L1–L5, EC 0x743–0x747)**, not 8; treat "8-point" as an aspirational abstraction. The GPU-mode switch (Hybrid / dGPU / iGPU) is **unvalidated / future work** — no confirmed EC register, not in the write allowlist.

---

## 6. At-a-glance (Tier 1) vs on-demand (Tier 2)

Two explicit tiers (the pattern all four reference tools converge on). Group by physical component, collapsible/reorderable — not a flat metric grid.

| | **Tier 1 — home (always visible)** | **Tier 2 — expander / on-demand** |
|---|---|---|
| **CPU** | load gauge, temp readout (center), effective clock, power+PL bar, status chip | per-core load, per-core temps, Vcore, CCD temps, reported vs effective clock, min/max/avg + reset |
| **GPU** | load gauge, temp readout, core clock, board power+TGP bar, status chip | Hot-Spot (labeled 2ndary), memory clock, voltages, min/max/avg + reset |
| **Memory** | RAM %+bar, Commit %+bar, VRAM %+bar (3 cards) | adapter-wide "allocated" VRAM, shared-VRAM detail, Cached/Standby/Modified, Committed/Paged/Non-paged pools |
| **Fans** | CPU/GPU RPM + %max, active mode pill | 5-point (L1–L5) curve editor; GPU-mode switch is future/unvalidated |
| **Trend** | CPU+GPU temp overlay, 60 s | window presets (5/15 min), optional separate advanced chart for clock/power (never on the temp axis) |
| **Global** | — | per-sensor threshold config, CSV logging (interval + duration cap), persist layout/order/renames, disable heavy sensors (SMART/VRM) |

**Global conventions:** poll at **1 Hz** (matches the trend; HWiNFO's 2 s default is fine to fall back to) with the polled set configurable and heavy sensors disable-able; keep Current/Min/Max/Avg per metric with a **global reset**; persist sensor selection, order, renames, and positions across sessions; provide a friendly display-name layer over raw driver strings.

**Files:** this is a standalone spec — no repo files were read or written.