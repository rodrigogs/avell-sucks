# Product

## Register

product

## Users

Rodrigo — single power user on his own Avell gaming laptop (Windows). Technical, opinionated, runs this instead of the OEM Gaming Center. Context of use: at the desk, adjusting fan/power/RGB before or during gaming, and glancing at live telemetry while the machine is under load. Fluent enough to expect the tool to be fast, honest about hardware state, and never lie about whether a write succeeded.

## Product Purpose

**AvellSucks** — a safer, modern replacement for the Avell/OEM Gaming Center. The
shipped face is a WPF/.NET 10 app driving the hardware in-process over EC/WMI (an
optional loopback ASP.NET API mirrors the same surface for automation):

- **Fan / performance**: modes (auto, boost, custom, L1–L5) and a custom temp→PWM curve.
- **Power limits**: PL1 / PL2 / PL4 (EC 0x783/0x784/0x785), gated behind an EC write allowlist + audit.
- **RGB keyboard**: ITE HID (VID 0x0489 / PID 0xCE40) — *planned/stubbed*. Effects (static, breathing, color cycle, wave, ripple), brightness, speed, per-zone color are designed and wired, but the backend is unfinished/untested because the laptop's keyboard is physically broken (see the README's keyboard note); it ships as a stub, parked in `app/_pending/rgb-hid/`.
- **Live telemetry**: CPU/GPU temps, fan RPM, memory, power draw (also streamed over SSE at `/events` by the optional server).

Success = it replaces the OEM app entirely: prettier, trustworthy, quick to actuate, and it never renders off-screen or hides a failed hardware write.

## Brand Personality

Cyberpunk performance instrument. Three words: **charged, precise, alive**. Neon energy (magenta→cyan on deep violet-black) but disciplined — the glow serves live data and active state, never decoration. Confident and a little dangerous, like overclocking software that respects the silicon. Honest above all: a gated/failed EC write must read as blocked, not faked.

## Anti-references

- **The OEM Gaming Center itself** — heavy, dated, cheap skew/bevel gradients, bloatware feel. This project exists to kill it.
- **Generic gamer clichés** — no decorative hexagons, dragons, carbon-fiber textures, angular "tech" display fonts, no RGB rainbow smeared across every surface. Gamer energy lives where it's real: the actual keyboard RGB and live-load states.
- **Corporate SaaS / admin panel** — no identical card grids, no soulless corporate blue, no dashboard-template hero-metric row.

## Design Principles

1. **Glow follows data.** Neon and bloom mark live values, active modes, and state changes — never inert chrome. A dark, calm surface makes the charged parts read.
2. **Honesty of hardware state.** Every control shows real, current EC/HID state and the truth of a write (allowed / executed / verified / blocked). Never optimistic UI over an unconfirmed write.
3. **Instrument, not toy.** Gauges and readouts are precise and legible under a glance during load. Monospaced numerics, real units, calm when idle.
4. **Actuate fast.** The primary job on each tab is one confident action (set mode, apply curve, apply RGB). Minimize ceremony between intent and hardware.
5. **Danger is legible.** Power-limit and EC writes are the sharp edges; the UI makes their risk and gated status visible, never buries it.

## Accessibility & Inclusion

- Single-user desktop app, but hold WCAG AA on the dark theme: body text ≥4.5:1, large/numeric readouts ≥3:1 against the violet-black surfaces. Neon accents that carry meaning must also pass, not just glow.
- Never encode state by hue alone (active/blocked/error) — pair color with label, icon, or shape, so a red/green fan-state isn't the only signal.
- Respect reduced-motion: live glow pulses and gauge sweeps degrade to static fills / instant value changes.
