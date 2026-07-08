# AvellSucks

An unofficial, aftermarket **control center for a specific Avell gaming laptop**
(RODRIGO-AVELL, Windows) — fan, CPU power, Windows power plan, and RGB — talking
straight to the hardware, honestly.

It exists because the vendor's own app stopped being an option.

---

## Why this exists

I bought a top-of-the-line Avell gaming laptop in 2018. Years later the OEM
"Gaming Center" — the app that owns the fan curve, the performance modes, the
keyboard lighting, the whole thermal/power personality of the machine — was
**discontinued and abandoned**: dated, heavy, no longer maintained, and the only
sanctioned way to drive the laptop's own hardware.

So the choice was to live with abandoned bloatware sitting between me and my own
silicon, or replace it. This is the replacement. The name is not subtle on
purpose — it names the reason the project had to exist.

**AvellSucks does what the OEM app did, better and honestly:** it reads and writes
the same Embedded Controller (EC) registers the vendor used, switches the same
Windows power schemes, and never lies about whether a hardware write actually
landed.

---

## What it does

- **Fan** — modes (auto, boost, custom, L1–L5) and a custom temperature→PWM
  curve. Applies live as you edit; no Apply button.
- **Performance** — four modes (Gaming / High / Balanced / Saving) that switch
  the active **Windows power plan** and write the CPU power-limit bytes (PL1/PL2/PL4).
- **RGB** — keyboard lighting surface (ITE HID). *Backend not wired yet — the
  test unit's keyboard is dead; the UI and contract are in place.*
- **Dashboard** — live CPU/GPU load, temps, clocks, memory, disk, network, and
  the active cooling profile, streamed ~1 Hz.
- **Reactive** — changes made outside the app (the old OEM tool, the physical Fn
  fan key, another power-plan switcher) are reflected here within a couple of
  seconds. It mirrors the device; it never assumes its own last write is still true.

**Brand:** a cyberpunk performance instrument — *charged, precise, alive*. Neon
magenta→cyan on deep violet-black. Confident, a little dangerous, honest above
all. (See `PRODUCT.md` and `DESIGN.md`.)

---

## How it works (technical)

Everything was reverse-engineered from the decompiled OEM app + live hardware
probing. Findings:

### EC access — WMI ACPI test interface
The OEM never used a custom driver. All fan/power state lives in **Embedded
Controller RAM**, reached through a WMI ACPI method on `root\WMI`:
`AcpiTest_MULong.GetSetULong` (instance `ACPI\PNP0C14\1_1`).

- **Read:**  `Data = 0x100_0000_0000 | addr`  (2^40 + addr), return value is the byte.
- **Write:** `Data = (value << 16) | addr`  — **no read flag** (including it makes
  the EC silently ignore the write; this cost a debugging session to find).

### Confirmed registers
| Addr | Meaning |
|---|---|
| `0x751` (1873) | fan control byte — 0 auto, 0x40 boost, 0xA0 custom, 0x81–0x85 L1–L5 |
| `0x743`–`0x747` (1859–1863) | custom PWM levels |
| `0x783`/`0x784`/`0x785` (1923–1925) | PL1/PL2/PL4 setting bytes (watts) |
| `0x730`–`0x732` / `0x734`–`0x736` | Gaming / Office PL defaults (read-only) |

Note: on this board the PL registers read `0` — the real CPU limits are managed
by **Intel XTU / MSR**, not the EC — so the Performance tab shows nominal preset
watts and the mode's primary lever is the Windows power plan.

### Power plans
The four performance modes map 1:1 to dedicated Windows schemes the machine ships
(`MyGamingMode` / `MyHighPerformance` / `MyBalanced` / `MyPowerSaving`), switched
via `powercfg /setactive`. (The OEM app itself never switched schemes — that's a
gap this fills.)

### Safe write pipeline
Every EC write goes through `SafeEcWriter`:
**gate → allowlist → before-snapshot → write → read-back verify → rollback on
mismatch → JSONL audit.** A blocked/failed write shows as blocked/failed in the
UI — never faked. Control-register read-backs tolerate the firmware's transient
status bits and retry with backoff (the EC swallows writes mid-transition, esp.
leaving Boost).

### Architecture
.NET 10 solution (`app/AvellSucks.Replacement.slnx`):
- `AvellSucks.Core` — hardware contracts, safe-write pipeline, models (portable).
- `AvellSucks.Core.Windows` — `WmiEcBackend` (WMI EC read/write).
- `AvellSucks.Api` / `AvellSucks.Server` — optional local ASP.NET control API
  (loopback-only) exposing `/api/fan/*`, `/api/system/snapshot`, `/events` (SSE).
- `AvellSucks.UI` — the WPF app (dark, cyberpunk), telemetry via
  LibreHardwareMonitor, reactive reconcilers per tab.

---

## Using it

**Requirements:** Windows on the Avell, .NET 10 SDK, run **as Administrator**
(ring-0 sensor access + WMI EC writes need it). WPF → Windows-only.

```powershell
# from the app directory
dotnet build AvellSucks.Replacement.slnx

# run the WPF app (elevated)
dotnet run --project src/AvellSucks.UI

# or the local control server + API
dotnet run --project src/AvellSucks.Server -- 5055
```

**Write gate:** elevated ⇒ hardware writes are **on by default**. Override with
the env var `GAMINGCENTER_ALLOW_EC_WRITES` (`0`/`false` forces off — preview/demo
mode; `1` forces on for a non-elevated dev process). *(The env var keeps its
original name for compatibility with the server pipeline.)*

**Tests:** `dotnet test AvellSucks.Replacement.slnx` (82 tests — safe-write
pipeline, allowlist, write gate, fan controller, audit log).

**Perf note:** run from a **local disk** copy, not the WSL UNC path — loading
assemblies over `\\wsl.localhost\...` adds ~9s to startup.

---

## Safety

This writes low-level hardware registers. The allowlist restricts *which*
(address, value) pairs are permitted; every write is verified by read-back and
rolled back on mismatch, and audited to JSONL. Power-limit and EC writes are the
sharp edges — the UI makes their gated/blocked/failed state legible, never buries
it. Use on the hardware it was built for.

---

## RE artifacts

- `inventory.json`, `process-detail.json`, `ec-read-probe.json` — inventory + EC probes.
- `analysis/*.strings.txt` — static string extraction.
- `scripts/*.ps1` — reproducible Windows inventory/probe scripts.
- `notes/`, `DESIGN.md` (reactive-architecture spec) — RE + design notes.
- Decompiled OEM source (local, not in repo): `C:\Users\rdp\RE-tools\gaming-center-decompiled`.
