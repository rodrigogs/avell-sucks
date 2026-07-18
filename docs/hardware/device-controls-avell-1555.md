# Device controls — Avell 1555

This document defines the model-specific device-control surface implemented by
AvellSucks. It is intentionally narrow: every target below was identified on the
physical `Avell High Performance / 1555` machine and exercised through the same
Core/Core.Windows pipeline used by the UI, REST API and MCP server.

## Fail-closed model scope

Mutations are enabled only when `Win32_ComputerSystem` reports:

- manufacturer containing `Avell`; and
- model `1555` or `G1555`.

Unknown models can read a truthful unsupported status but cannot reach either the
EC sequence or a PnP mutation. This check is additional to the hardware-write gate,
remote-write authorizer and administrator requirement.

## Implemented controls

| Control | Mechanism | Verified target | Result semantics |
|---|---|---|---|
| Wi-Fi + Bluetooth | EC state `0x47B`, mask `0xA0`; edge/pending trigger `0x7A1`, mask `0xA0`; Windows PnP rescan afterwards | Intel Wireless-AC 9560 (`PCI\\VEN_8086&DEV_A370`) and Intel Bluetooth (`USB\\VID_8087&PID_0AAA`) | `Verified` only after the EC state bits settle; trigger read-back is intentionally not required because firmware consumes those bits |
| Touchpad | `CM_Disable_DevNode` / `CM_Enable_DevNode` through `cfgmgr32.dll` | Parent `ACPI\\UNIW0001\\1` (I2C HID Precision Touchpad) | `Verified` after `Win32_PnPEntity.ConfigManagerErrorCode` changes between 0 and 22 |
| Webcam | Same Configuration Manager path | USB interface prefix `USB\\VID_5986&PID_069E&MI_00\\` | `Verified` through ConfigManager problem-code read-back |
| Panel brightness | `root\\WMI:WmiMonitorBrightnessMethods.WmiSetBrightness` | Internal panel `DISPLAY\\NCP000B...` (`LM156LF1L02`) | `Verified` only when `WmiMonitorBrightness.CurrentBrightness` matches |
| Turn display off | `WM_SYSCOMMAND / SC_MONITORPOWER / 2` via `SendMessageTimeout` | Interactive Windows desktop | `Requested`, not `Verified`: Windows has no reliable physical-panel read-back |

The display operation deliberately fails in Session 0. Running the REST/MCP server
as a Windows service cannot power off the user's interactive display; the in-process
WPF UI can.

## Radio sequence and rollback

The GJ5CN firmware treats the two radio bytes differently:

1. Read complete baselines for `0x47B` and `0x7A1`.
2. Modify only WLAN bit `0x80` and Bluetooth bit `0x20` in `0x47B`.
3. Write the same mask to `0x7A1` to trigger firmware reconciliation.
4. Reconcile the Windows device tree and re-read `0x47B`.
5. On a state-write failure, reconciliation failure, exception, or unsettled state,
   restore the original `0x47B` value and retrigger it while preserving unrelated
   bits from the original `0x7A1` baseline.

`0x7A1` is consumed asynchronously by the EC. Exact trigger-byte read-back would
produce false failures, so only transport success is required for that write. The
state byte remains the source of truth.

Rollback success is reported only after exact `0x47B` read-back and successful
Windows reconciliation. Otherwise the audited failure says that rollback was
attempted but could not be verified. A zero-valued baseline is valid and is not
used as a sentinel.

## Safety gates and audit

Every mutation passes:

1. model identity check;
2. local hardware-write `WriteGate`;
3. REST/MCP `RemoteWriteAuthorizer` for non-loopback callers;
4. operation-level JSONL audit.

Audit file:

```text
%ProgramData%\\AvellSucks\\audit\\machine-control-audit.jsonl
```

Results use explicit outcomes: `Blocked`, `Failed`, `Requested`, or `Verified`.
A dispatched display command is never mislabeled as physically verified.

## Exposed surfaces

### WPF

The **Devices** tab exposes wireless radios, touchpad, webcam, panel brightness and
display-off. `GC_START_TAB=devices` deep-links to it for diagnostics.

### REST

```text
GET  /api/devices
POST /api/devices/wireless       { "enabled": true }
POST /api/devices/touchpad       { "enabled": true }
POST /api/devices/webcam         { "enabled": true }
POST /api/devices/brightness     { "percent": 75 }
POST /api/devices/display/off
```

### MCP

```text
get_machine_controls
set_wireless_radios
set_touchpad_enabled
set_webcam_enabled
set_panel_brightness
turn_off_display
```

## Real-machine validation

The elevated `GC_SELFTEST=machine-controls` diagnostic exercised the shipped UI
composition root and returned verified results for:

- radio ON reconciliation;
- touchpad OFF then ON;
- webcam OFF then ON;
- brightness 75 → 74 → 75.

The final snapshot matched the initial state: radios on, touchpad on, webcam on and
brightness 75. Each reversible operation uses a `finally` restore in the diagnostic.
Use `GC_SELFTEST=machine-display-off` only from an interactive session; it also
sends the display-off request.

## Investigated but intentionally not exposed

The OEM software and firmware also contain evidence for:

- webcam EC bit `0x47B/0x7A1` mask `0x10` — not used because PnP is safer and verified;
- PS/2 touchpad EC bit mask `0x02` — not applicable to the active I2C touchpad;
- WWAN/global-airplane bits — hardware absent or command semantics incomplete;
- light-bar registers `0x748..0x74B` — plausible but not validated on working hardware;
- USB charging, Win-key lock and silent mode — actual action remains inside the
  unavailable OEM `MySettingDLL`, so the command contract is not proven;
- DDC/CI — the internal panel returned `ERROR_NOT_SUPPORTED`;
- `SetDisplayConfig` — topology mutation is too heavy for a simple display-off action.

Do not turn OSD event constants into commands. They report firmware events; they do
not define a safe write protocol.
