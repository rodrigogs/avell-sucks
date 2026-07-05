# Dynamic probe plan: safe read-only diffs while OEM UI changes state

Status: READ-ONLY observation. No writes, no invasive tracers unless Rodrigo approves a follow-up.

## Goal

Correlate human-visible state changes in the OEM GamingCenter UI with EC register reads via WMI `AcpiTest_MULong.InstanceName='ACPI\PNP0C14\1_1'`.

## Starting point

Existing low-level helper: `scripts/read-ec-probe.ps1`

Snapshot + diff helper: `scripts/ec-snapshot.ps1`

Guided runner: `scripts/run-dynamic-probe.ps1`

Recommended runner:
- gui-friendly when human input is needed,
- no arguments shows guidance,
- supports automated test batches with `-AutoBaseline -AutoAfter -AutoDiff -HumanAction "..."`.

## Operational rules for Rodrigo

Keep investigation non-invasive unless explicitly approved.

1. Close other changing apps/logging tools if possible.
2. Run a first snapshot with the UI in the known state.
3. Make exactly ONE change in the OEM UI.
4. Run the second snapshot immediately.
5. Diff the two snapshots.

## Usage

### Interactive guidance

```powershell
.\scripts\run-dynamic-probe.ps1 -WorkDir .\probe-runs
```

### Automated baseline + after + diff

```powershell
.\scripts\run-dynamic-probe.ps1 -WorkDir .\probe-runs -AutoBaseline -AutoAfter -AutoDiff -HumanAction "Set Performance: Fan Boost" -Label myrun
```

### Manual snapshot

```powershell
.\scripts\ec-snapshot.ps1 -Label baseline -OutFile .\snapshots\before.json
```

```powershell
.\scripts\ec-snapshot.ps1 -Label after -HumanAction "Set Performance: Fan Boost" -OutFile .\snapshots\after.json
```

```powershell
.\scripts\ec-snapshot.ps1 -Diff -DiffBefore .\snapshots\before.json -DiffAfter .\snapshots\after.json -OutFile .\snapshots\diff.json
```

### Optional wider scan

```powershell
.\scripts\ec-snapshot.ps1 -Label wide -ExtraAddresses 0x800,0x801,0x802 -OutFile .\snapshots\wide.json
```

## Observed address map

| Address | Hex      | Current value | Likely meaning                         |
|-------: |----------|---------------|----------------------------------------|
| 1857    | 0x741    | 0x201 / 513   | RGB/key event flags                     |
| 1858    | 0x742    | 0x2 / 2       | support byte / MyFanboost2 flag bit 1   |
| 1859    | 0x743    | 0x0 / 0       | customize L1 PWM source                 |
| 1860    | 0x744    | 0x7800/30720  | customize L2 PWM source                 |
| 1861    | 0x745    | 0x8C78/35960  | customize L3 PWM source                 |
| 1862    | 0x746    | 0x8C8C/35980  | customize L4 PWM source                 |
| 1863    | 0x747    | 0x8C / 140    | customize L5 PWM source                 |
| 1873    | 0x751    | 0x6CA0/27808  | main fan control byte                   |
| 1885    | 0x75D    | 0x1200/4608   | trigger byte 2                          |
| 1893    | 0x765    | 0xFFFF/65535  | support byte 1                          |
| 1894    | 0x766    | 0xFF / 255    | support byte 2                          |
| 1895    | 0x767    | 0x200/512     | trigger byte                            |
| 1896    | 0x768    | 0x2 / 2       | status byte                             |
| 1922    | 0x782    | 0x0 / 0       | BIOS OEM byte 2                         |

## Human input points

Use these UI actions as discrete observation events.

- Fan mode: normal / fan boost / custom 1..5 / custom advanced
- RGB effect: cycle through presets, colors, speed
- Lightbar on/off
- MyFanboost2 support flag

After each action, record the action tag in `-HumanAction` and save a fresh after-snapshot.

## Optional deeper observation plan (do not run yet)

Do not attach invasive tracers or write EC without explicit approval.

- ProcMon capture on `GamingCenter.exe` with WMI filter.
- Frida instrumentation of `HardwareAccess.dll` and `ECIO.dll` for WMI marshaling.
- Event watcher for `AcpiTest_EventULong` to see async EC updates without polling.

## Safety boundaries

- Read-only until approved.
- Do not write address 1873 or PWM range 1859..1863 without explicit approval.
- Stop immediately on WMI/ACPI access errors from insufficient privilege.
- Prefer this read-only helper before any kernel/hardware hook.
