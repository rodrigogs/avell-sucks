# Reverse-engineering: validated hardware knowledge

> Everything here was **confirmed on the real Avell hardware** (or read straight
> from the decompiled OEM app and then exercised through our own code). Speculation
> that turned out wrong is quarantined in [§8](#8-disproven--superseded). Raw
> evidence lives in [`evidence/`](evidence/) and [`archive/`](archive/).
>
> **"GamingCenter" = the OEM Avell/Uniwill app** we replaced; **"AvellSucks" = this
> project.** Register addresses are given as decimal + hex.

## 1. Target & provenance

OEM stack (Uniwill/Intel-XTU-derived, Avell-rebadged): `GamingCenter.exe` +
`GamingCenterTray.exe` (WPF/.NET Framework), `LaunchServGM.exe` (service
`GamingCenter`), `XtuService.exe` (`XTU3SERVICE`), `OSDTpDetect.exe`. Settings
under `HKLM\SOFTWARE\OEM\GamingCenter`. Native/driver layer: `ECIO.dll`,
`inpoutx64.DLL`, `HardwareAccess.dll` (Confuser-protected; Intel IronCity —
OcMailbox/MSR/TurboRatio/TDP). PDB path evidence: `D:\Max\Source\GamingCenter\...`.
Decompiled with `ilspycmd`. Full recon: [`evidence/inventory.json`](evidence/inventory.json),
[`evidence/process-detail.json`](evidence/process-detail.json). Verbatim string dumps
from the OEM binaries are intentionally **not** redistributed — see
[§9](#9-evidence-index).

## 2. EC access primitive (CONFIRMED)

WMI on `root\WMI`: class `AcpiTest_MULong`, instance `ACPI\PNP0C14\1_1`, method
`GetSetULong`. Events via `AcpiTest_EventULong`.

| Operation | Encoding |
|---|---|
| **Read**  | `Data = 0x100_0000_0000 \| addr`  (2^40 + addr); return value is the byte |
| **Write** | `Data = (value << 16) \| addr`  — **no read flag** |

Values are single-byte (`& 0xFF`). Including the read flag on a write makes the EC
**silently ignore it** (cost a debugging session to find). The BIOS/ODM path
(`AcpiODM_Demo`, `PNP0C14\2_0`, `GetUlongEx7`) exists but is **high-risk and
unused**. Cross-ref: `AvellSucks.Core.Windows/WmiEcBackend.cs`.

## 3. Fan control (CONFIRMED)

Control byte **0x751 (1873)** = `ADDR_MAFAN_CONTROL_BYTE`:

| Byte | Mode |
|---|---|
| `0x00` (0) | auto / normal |
| `0x40` (64) | boost |
| `0xA0` (160) | custom (advanced, follows the curve) |
| `0x81`–`0x85` (129–133) | fixed levels L1–L5 |

Custom curve PWM sources: **0x743–0x747 (1859–1863)**, one byte per level, max
`0x8C` (140). Default temperature anchors: `[50, 60, 70, 80, 90] °C`. Capability
byte `0x782`. OEM string corroboration: `ADDR_MAFAN_CONTROL_BYTE`,
`ADDR_MYFAN2_L1_PWM..L5_PWM`, `FAN_MODE_NORMAL/BOOST/CUSTOMIZE`,
`FAN_LEVEL_ONE..FIVE`. Single source of truth in code:
`AvellSucks.Core.Hardware/FanModeMap.cs` (the allowlist and every service read
from it).

## 4. CPU power limits (CONFIRMED)

EC registers **0x783 / 0x784 / 0x785 (1923 / 1924 / 1925)** = **PL1 / PL2 / PL4**
in watts (`value & 0xFF`). OEM vocab: `SHORT/EXTENDED_WINDOW_..._DESIGN_POWER_LIMIT`,
`SetPL124Tau`. Cross-ref: `PowerController.cs`, `WmiPowerService.cs`,
`EcWriteAllowlist.cs`.

Caveat on *this* board: those PL registers read `0` — the effective CPU limits are
managed by **Intel XTU / MSR**, not the EC. So the Performance tab shows nominal
preset watts, and the mode's real lever is the Windows power plan (§5).

## 5. Power plans (CONFIRMED — our addition, not the OEM's)

AvellSucks switches Windows power schemes with `powercfg /setactive`, mapping the
four performance modes 1:1 to the machine's own schemes: **MyGamingMode /
MyHighPerformance / MyBalanced / MyPowerSaving**. Note: the **OEM app does not use
Windows power plans** — that's an observation about OEM behavior, not a constraint;
switching them is a gap we fill. Cross-ref: `WindowsPowerPlan.cs`.

## 6. RGB keyboard (OEM path documented; our backend is a stub)

ITE HID: **VID 0x0489 / PID 0xCE40**, UsagePage `0xFF00`/`0xFF01`. Report codes:
`0x08` effect, `0x12` load-picture, `0x14` per-index color, `0x16` row index, `0x88`
read-effect, `0x80` firmware version (`HID_Set_Color_14H`, `ILM_RGBKB_*`, etc.).
KB-type discovery via UsagePage + registry `KBTypeID` (65298 FourZone / 65282
MEZone_1st / 65283 MEZone_2nd).

Our implementation is **unfinished and untested** — the laptop's keyboard ribbon
failed (see the README's keyboard note), so there's no hardware to verify against.
The code is parked in [`../app/_pending/rgb-hid/`](../app/_pending/rgb-hid/); the UI
ships the `LocalRgbService` stub (`Available = false`).

## 7. Write-safety model (CONFIRMED shipped)

Every EC write goes through `SafeEcWriter`:

```
gate (GAMINGCENTER_ALLOW_EC_WRITES) → allowlist → before-snapshot → WMI write
→ read-back verify (settle + retry) → rollback on mismatch → JSONL audit
```

It **never throws** — every outcome (denied, no-baseline, write-threw, mismatch,
verified) is returned as a result and audited. The settle+retry backoff exists
because the EC swallows writes mid-transition (notably leaving Boost), confirmed by
probing. Cross-ref: `SafeEcWriter.cs`, `EcWriteAllowlist.cs`, `WriteGate.cs`.

## 8. Disproven / superseded (do not re-make these)

- ❌ **Tau as an EC register** (`0x741`/1857, "ADDR_MYFAN3_CPU_TAU"). Tau is an
  Intel XTU/MSR setting (OEM `-id 66`); the EC reads it as 0. `WmiEcBackend`
  reports `TauSeconds: 0` deliberately.
- ❌ **Speculative PL registers `0x77F–0x781` (1919–1921).** Wrong neighborhood;
  the real ones are `0x783–0x785`. The API once wrote the wrong set and every
  request 403'd against the allowlist.
- ❌ **`0x80` (128) as a live "user fan" mode.** It's a legacy base; the real
  custom bytes are `0x81–0x85`. `FanModeMap` has no 128 entry.
- ⚠️ **Raw probe WORD reads** (e.g. `1873 = 0x6CA0`). Reads returned 16-bit words;
  only the low byte is the register. Treat probe tables as point-in-time evidence,
  not spec.

## 9. Evidence index

- [`evidence/ec-read-probe.json`](evidence/ec-read-probe.json) — early read-only EC snapshot (pre-PL discovery).
- [`evidence/inventory.json`](evidence/inventory.json) — OEM process/service/driver recon.
- [`evidence/process-detail.json`](evidence/process-detail.json) — per-process detail.
- ~~`evidence/strings/`~~ — verbatim string dumps from the proprietary OEM binaries.
  **Removed from the public repo for licensing.** Regenerate locally with
  `strings`/`ilspycmd` if you want to reproduce the RE; the findings themselves are
  summarized above and in [`archive/research-re-study.json`](archive/research-re-study.json).
- [`archive/research-re-study.json`](archive/research-re-study.json) — the full OEM RE dossier.
- [`archive/re-log-2025-07.md`](archive/re-log-2025-07.md) — the original hand-written RE notes.
