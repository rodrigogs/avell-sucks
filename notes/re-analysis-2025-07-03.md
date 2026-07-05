# Gaming Center reverse-engineering notes

Scope: `GamingCenter` decompiled sources. Last updated: 2025-07-03

## Subsystem map

### EC WMI bridge
- `WMIReadECRAM(addr)` / `WMIWriteECRAM(addr, value)` in `oobeview/WMIEC.cs`.
- Write encoding: `value << 16 | addr`; read encoding: `0x1000000000000 | addr`.
- Backed by `root\WMI` -> `AcpiTest_MULong` WMI method `GetSetULong`.

### EC RAM layout constants (`ECSpec.cs`)
- `ADDR_MAFAN_CONTROL_BYTE = 1873`
- `ADDR_MYFAN2_L1_PWM = 1859`, `...L2 = 1860`, `...L3 = 1861`, `...L4 = 1862`, `...L5 = 1863`
- `ADDR_L1_PWM_DEFAULT_MYFAN2 = 1929`, `...L2 = 1930`, `...L3 = 1931`, `...L4 = 1932`, `...L5 = 1933`
- `ADDR_BIOS_OEM_BYTE2 = 1922`
- `ADDR_AP_OEM_BYTE = 1857`
- Support checks: `ADDR_SUPPORT_BYTE5 = 1858`, `ADDR_TRIGGER_BYTE2 = 1885`
- AC/BT temperature/battery: `ecBt1Temperature = 1186`, `ecBt1RSOC = 1195`, `ecPowSource = 1168`

### OEM service bridge
- `OemServiceModel/OemService.cs`
- Two interop paths:
  - DLL path: `NativeMethod.OemSvcHook` DllImport -> `OemService.Exec` marshals output bytes.
  - EXE path: `OemService.Read` / `OemService.Write` spawn `OemServiceWinApp.exe`.
- Commands used:
  - `setapctrl /GetStatus` -> probe features once at init.
  - `ledkb /getstatus`, `ledkb /setdata 0x08 ...`, `ledkb /setdata 0x1A ...` -> RGB KB
  - `RGBLB /DUMP` -> lightbar support probe

### USB HID path for ITE RGB KB
- `HIDManager` finds HID with `VID=0x0489`, `PID=0xCE40`, `UsagePage` `0xFF00`/`0xFF01`, usage `0x01`.
- `HidD_SetFeature` / `HidD_GetFeature` with 9-byte feature reports.
- 65-byte `m_HIDDevice.Write` output reports to FileStream for per-row color.

### HID report IDs (`LM_ITE_RGB.cs`)
- `0x08` effect control
- `0x12` load picture
- `0x14` per-index color
- `0x16` row index
- `0x88` read current effect state
- `0x80` firmware version cmd/response

### RGB KB dispatch
- `LM_Manager` tries `LM_ITE_RGB` first, then `LM_EC_RGB`.
- ITE KB types inferred from `UsagePage` and registry `KBTypeID`
  - `65298` => FourZone
  - `65282` => MEZone_1st
  - `65283` => MEZone_2nd_{101,102}
- Special color remap for MEZone 2nd and some FourZone projects through `WKDColor.cheatRGB_*`.
- Effect mapping documented in `Translate_ITE_EffectIndex` / `Translate_LM_EffectIndex`.

### Fan control (two implementations)
- `FanManagementPage` legacy path: direct WMI EC RAM writes to `ADDR_MAFAN_CONTROL_BYTE`.
  - Normal/Basic/Advanced/Customize modes, welcome effect, draft feel-only UI.
- `FanManagementPage2` newer path: `IOdriverEC` driver loads/unloads around reads/writes, supports PL1/PL2/PL4+Tau tables and LevelTemp tables.
  - Registry state: `HKLM\OEM\GamingCenter\MyFan3`

### Boot init
- `InitGamingCenter.Init` parses `setapctrl /GetStatus` fixed offsets into ProjectID, KBTypeID, feature bits; writes feature flags under `HKLM\OEM\GamingCenter\Support`.

## Outstanding ambiguity
- Many `catch {}` blocks swallow exceptions silently; exact OEM driver error channels unknown.
- `RGBLB /DUMP` field offsets were inferred from fixed substring indices; need validation.
- EC write high-word semantics likely vendor-defined; we only know app-level encoding.

## Evidence appendix

### WMI EC encoding
```text
Addr = 1099511627776L + Addr;
Value <<= 16;
Addr = Value + Addr;
```

### HID ITE init
```text
if (m_HIDManager.Init(1165, 52736, 1))
{
    switch (m_HIDManager.GetUsagePage())
    {
        case 65298: m_ITE_KB_Type = RGBKB_Type.FourZone; break;
        case 65282: m_ITE_KB_Type = RGBKB_Type.MEZone_1st; break;
        case 65283: /* KBTypeID lookup */ break;
    }
    ...
    m_HIDDevice = new FileStream(new SafeFileHandle(...), FileAccess.ReadWrite, 65, isAsync: true);
}
```

### Fan writes
```text
WMIEC.WMIWriteECRAM(1873uL, 0uL);   // Normal
WMIEC.WMIWriteECRAM(1873uL, 64uL);  // Boost
WMIEC.WMIWriteECRAM(1873uL, 160uL); // Customize advanced
WMIEC.WMIWriteECRAM(1873uL, 128uL + level); // Basic level L1-L5
```

### OEM service invocation
```text
ProcessStartInfo.Arguments = "setapctrl /GetStatus";
ProcessStartInfo.Arguments = "RGBLB /DUMP";
OemService.Write($"ledkb /setdata 0x08 {control} {effect} ...");
```

### Summary of reputation
- This is a WPF OEM tuning app for what appear to be Mechrevo/Origin PC/DNS/Clevo-derived designs.
- Code quality is typical shipped OEM tool: UI-heavy, some unsafe parsing of fixed-offset text output, broad exception swallowing.
- Still useful as a behavioral spec for ACPI EC WMI register usage, ITE HID RGB, OEM service CLI protocol, and fan mode state machines.
