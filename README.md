<div align="right">

**English** · [Português](README.pt-BR.md)

</div>

<div align="center">

<img src="docs/assets/logo.png" alt="AvellSucks" width="120" height="120" />

# AvellSucks

**An unofficial control center for the Avell gaming laptop, fan, CPU power, Windows power plan and RGB, talking straight to the hardware, honestly.**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20x64-141018?labelColor=1c1622)](#install)
[![.NET](https://img.shields.io/badge/.NET-10.0-A855F7?labelColor=1c1622)](https://dotnet.microsoft.com)
[![UI](https://img.shields.io/badge/UI-WPF-22D3EE?labelColor=1c1622)](#)
[![i18n](https://img.shields.io/badge/i18n-EN%20%7C%20PT--BR-FF2E88?labelColor=1c1622)](#languages)
[![Tests](https://img.shields.io/badge/tests-82%20passing-34E5A0?labelColor=1c1622)](#build-from-source)

<sub>Unofficial · not affiliated with, endorsed by, or supported by Avell. Use on the hardware it was built for.</sub>

</div>

<h4 align="center">
  <a href="#why-this-exists">Why</a> &nbsp;·&nbsp;
  <a href="#what-it-does">Features</a> &nbsp;·&nbsp;
  <a href="#install">Install</a> &nbsp;·&nbsp;
  <a href="#how-it-works">How it works</a> &nbsp;·&nbsp;
  <a href="#safety">Safety</a> &nbsp;·&nbsp;
  <a href="#build-from-source">Build</a>
</h4>

<div align="center">

<img src="docs/assets/dashboard.png" alt="AvellSucks dashboard" width="820" />

</div>

---

## Why this exists

I bought a top-of-the-line Avell gaming laptop in 2018. By the time Windows 11
got its first big update (version 22H2, released **September 20, 2022**, which
Microsoft then supported for all of **24 months**), the OEM "Gaming Center", the
app that owns the fan curve, the performance modes, the keyboard lighting, the
whole thermal and power personality of the machine, was **discontinued and
abandoned**: dated, heavy, no longer maintained, and still the only sanctioned
way to drive the laptop's own hardware. Roughly four years of ownership, and the
software that ran my machine was already dead.

So the choice was to live with abandoned bloatware sitting between me and my own
silicon, or replace it. This is the replacement. The name is not subtle on
purpose: it names the reason the project had to exist.

**AvellSucks does what the OEM app did, better and honestly:** it reads and writes
the same Embedded Controller (EC) registers the vendor used, switches the same
Windows power schemes, and never lies about whether a hardware write actually
landed.

## The keyboard (why RGB is untested)

There's a second reason this laptop left a sour taste, and it's why the RGB tab
ships unfinished and unverified.

Around two years into owning it, I opened the machine to clean it. Inside, the
keyboard's ribbon connector was cracked and held together with a piece of tape.
Not by me. From the factory. That's how it shipped.

By then the keyboard was already failing, and there was no real way to fix it. I
went to Avell. They had nothing to offer, the warranty had already expired, and
it made no difference that the defect was theirs from day one.

So the built-in keyboard on this machine no longer works. The RGB lighting code
(ITE HID) is written and wired into the UI, but I can't test it against a
keyboard that's dead. It stays behind an honest "unavailable" state until there's
hardware to prove it on.

## What it does

- **Fan**: modes (auto, boost, custom, L1-L5) and a custom temperature→PWM
  curve. Applies live as you edit; no Apply button.
- **Performance**: four modes (Gaming / High / Balanced / Saving) that switch
  the active **Windows power plan** and write the CPU power-limit bytes (PL1/PL2/PL4).
- **RGB**: keyboard lighting surface (ITE HID). UI and contract are in place,
  but the backend is unfinished and untested (see [above](#the-keyboard-why-rgb-is-untested)).
- **Dashboard**: live CPU/GPU load, temps, clocks, memory, disk, network and the
  active cooling profile, streamed ~1 Hz.
- **Reactive**: changes made outside the app (the old OEM tool, the physical Fn
  fan key, another power-plan switcher) show up here within a couple of seconds.
  It mirrors the device; it never assumes its own last write is still true.
- **Settings**: language, start-with-Windows, start-minimized, and
  hide-to-tray-on-minimize. Preferences persist to
  `%AppData%\AvellSucks\settings.json`.
- **Languages**: English and Portuguese, switchable live from Settings, no
  restart. The default follows your Windows display language: Portuguese on a
  pt/pt-BR system, English otherwise. Change it and the choice is remembered.

<sub>**Brand:** a cyberpunk performance instrument, *charged, precise, alive*. Neon magenta→cyan on deep violet-black.</sub>

## Install

> **Requires Windows 10/11 (x64) and administrator rights.** The app talks to the
> Embedded Controller and ring-0 sensors, so it must run elevated.

1. Download the latest **`AvellSucks-Setup.exe`** from the
   [**Releases**](https://github.com/rodrigogs/avell-sucks/releases/latest) page.
2. Run it. It installs per-machine into `Program Files`, adds a Start-menu
   shortcut, and registers an uninstaller in *Add or remove programs*.
3. Launch **AvellSucks** and approve the UAC prompt.

The app checks GitHub for newer releases and can update itself from
**Settings → Updates** (it downloads the new installer and relaunches silently).

> **Note, unsigned installer.** There's no code-signing certificate, so the
> first time you run `AvellSucks-Setup.exe` Windows SmartScreen will say
> *"Windows protected your PC / unknown publisher."* Click **More info → Run
> anyway**. This is expected for a personal, unsigned tool; later self-updates are
> applied by the already-trusted updater, so the prompt only appears on the first
> install.

### Start with Windows

The **Start with Windows** toggle registers a Scheduled Task with *highest
privileges* rather than a Run-key entry, that's the supported way to launch an
elevated app at logon **without** a UAC prompt every boot.

## How it works

Everything was reverse-engineered from the decompiled OEM app plus live hardware
probing.

### EC access: WMI ACPI test interface
The OEM never used a custom driver. All fan/power state lives in **Embedded
Controller RAM**, reached through a WMI ACPI method on `root\WMI`:
`AcpiTest_MULong.GetSetULong` (instance `ACPI\PNP0C14\1_1`).

- **Read:**  `Data = 0x100_0000_0000 | addr`  (2^40 + addr); the return value is the byte.
- **Write:** `Data = (value << 16) | addr` , **no read flag** (including it makes
  the EC silently ignore the write; this cost a debugging session to find).

### Confirmed registers
| Addr | Meaning |
|---|---|
| `0x751` (1873) | fan control byte, 0 auto, 0x40 boost, 0xA0 custom, 0x81-0x85 L1-L5 |
| `0x743`-`0x747` (1859-1863) | custom PWM levels |
| `0x783`/`0x784`/`0x785` (1923-1925) | PL1/PL2/PL4 setting bytes (watts) |
| `0x730`-`0x732` / `0x734`-`0x736` | Gaming / Office PL defaults (read-only) |

On this board the PL registers read `0`: the real CPU limits are managed by
**Intel XTU / MSR**, not the EC, so the Performance tab shows nominal preset
watts and the mode's primary lever is the Windows power plan.

### Power plans
The four performance modes map 1:1 to dedicated Windows schemes the machine ships
(`MyGamingMode` / `MyHighPerformance` / `MyBalanced` / `MyPowerSaving`), switched
via `powercfg /setactive`.

### Safe write pipeline
Every EC write goes through `SafeEcWriter`:
**gate → allowlist → before-snapshot → write → read-back verify → rollback on
mismatch → JSONL audit.** A blocked or failed write shows as blocked/failed in the
UI, never faked. Control-register read-backs tolerate the firmware's transient
status bits and retry with backoff (the EC swallows writes mid-transition, especially
leaving Boost).

### Architecture
.NET 10 solution (`app/AvellSucks.Replacement.slnx`):
- `AvellSucks.Core`: hardware contracts, safe-write pipeline, models (portable).
- `AvellSucks.Core.Windows`: `WmiEcBackend` (WMI EC read/write).
- `AvellSucks.Api` / `AvellSucks.Server`: optional local ASP.NET control API
  (loopback-only) exposing `/api/fan/*`, `/api/system/snapshot`, `/events` (SSE).
- `AvellSucks.UI`: the WPF app (dark, cyberpunk), telemetry via
  LibreHardwareMonitor, reactive reconcilers per tab. Runtime localization
  (`.resx` + a `Loc` provider and `{loc:Tr}` markup extension) switches language
  live; preferences persist as JSON under `%AppData%`, logs and the EC-write audit
  under `%ProgramData%\AvellSucks`.

## Safety

This writes low-level hardware registers. The allowlist restricts *which*
(address, value) pairs are permitted; every write is verified by read-back, rolled
back on mismatch, and audited to JSONL. Power-limit and EC writes are the sharp
edges, the UI makes their gated/blocked/failed state legible, never buries it.
Use on the hardware it was built for.

## Build from source

**Requirements:** Windows on the Avell, .NET 10 SDK, run **as Administrator**
(ring-0 sensor access + WMI EC writes need it). WPF → Windows-only.

```powershell
# from the app directory
dotnet build AvellSucks.Replacement.slnx

# run the WPF app (elevated)
dotnet run --project src/AvellSucks.UI

# or the local control server + API
dotnet run --project src/AvellSucks.Server -- 5055

# tests (82: safe-write pipeline, allowlist, write gate, fan controller, audit log)
dotnet test AvellSucks.Replacement.slnx
```

**Cutting a release:** push a version tag (`git tag v1.2.3 && git push origin
v1.2.3`). The [release workflow](.github/workflows/release.yml) publishes a
self-contained win-x64 build, compiles the Inno Setup installer, and attaches
`AvellSucks-Setup.exe` to a GitHub Release.

**Write gate:** elevated ⇒ hardware writes are **on by default**. Override with the
env var `GAMINGCENTER_ALLOW_EC_WRITES` (`0`/`false` forces off, preview/demo mode;
`1` forces on for a non-elevated dev process). *(The env var keeps its original
name for compatibility with the server pipeline.)*

**Perf note:** run from a **local disk** copy, not the WSL UNC path, loading
assemblies over `\\wsl.localhost\...` adds ~9 s to startup.

## Reverse-engineering artifacts

- `inventory.json`, `process-detail.json`, `ec-read-probe.json`: inventory + EC probes.
- `analysis/*.strings.txt`: static string extraction.
- `scripts/*.ps1`: reproducible Windows inventory/probe scripts.
- `notes/`, `DESIGN.md` (reactive-architecture spec), RE + design notes.

## License

Personal, unofficial project. **Not affiliated with Avell.** "Avell" and "Gaming
Center" are the property of their respective owners; used here only to describe
what this software is compatible with.
