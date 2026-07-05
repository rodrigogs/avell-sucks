# Initial reverse-engineering notes — GamingCenter

Generated: 2026-07-03

## Running components

Observed live processes:

- `GamingCenter.exe` — main WPF/.NET Framework 4.6.1 app at `C:\Program Files\OEM\GamingCenter\GamingCenter.exe`.
- `GamingCenterTray.exe` — tray app.
- `LaunchServGM.exe` — Windows service process for service `GamingCenter`.
- `XtuService.exe` — service `XTU3SERVICE`, likely Intel XTU integration.
- `OSDTpDetect.exe` and `OOBE\OOBEI2CTpOnOffDetect.exe` — startup helpers.

Startup entries:

- `GamingCenter`: `LaunchCtrlGM.exe -R`
- `OSDTpDetect.exe`
- `OOBEI2CTpOnOffDetect.exe`

No GamingCenter-owned TCP listener was found. Named-pipe scan did not show an obvious GamingCenter pipe. The app appears to communicate mostly through WMI/ACPI, registry, Windows messages, and direct HID/native DLL calls rather than HTTP/socket IPC.

## Binary type

DIE results:

- `GamingCenter.exe`: PE32, .NET Framework CLR 4.0.30319, Visual Studio, Authenticode signed.
- `GamingCenterTray.exe`: .NET/WPF style strings and `System.Management` usage.
- `ECIO.dll`: PE64 native C++ VS2017.
- `HardwareAccess.dll`: .NET Framework CLR 2.0, Confuser 1.x protected.

I installed/used `ilspycmd` 10.1.0.8386 under `C:\Users\rdp\RE-tools\ilspycmd` and decompiled key assemblies into:

`C:\Users\rdp\RE-tools\gaming-center-decompiled`

## Core control primitive discovered

The cleanest control path is WMI under `root\WMI`:

```csharp
new ManagementObject("root\\WMI", "AcpiTest_MULong.InstanceName='ACPI\\PNP0C14\\1_1'", null)
```

Read EC byte/word-ish values:

```csharp
Addr = 1099511627776L + Addr;
methodParameters["Data"] = Addr;
Return = InvokeMethod("GetSetULong", ...)["Return"];
```

Write EC values:

```csharp
Value <<= 16;
Data = Value + Addr;
InvokeMethod("GetSetULong", ...);
```

Event watcher:

```csharp
new ManagementEventWatcher(new ManagementScope("\\\\.\\Root\\WMI"), new WqlEventQuery("SELECT * FROM AcpiTest_EventULong"));
```

BIOS/ODM write path exists too:

```csharp
AcpiODM_Demo.InstanceName='ACPI\\PNP0C14\\2_0'
GetUlongEx7(Data = Value)
```

Treat BIOS/ODM writes as high risk.

## Read-only EC probe results

Read probe script: `scripts/read-ec-probe.ps1`.

Successful read-only values:

| Address | Hex | Value | ValueHex | Notes |
|---:|---:|---:|---:|---|
| 1857 | 0x741 | 513 | 0x201 | RGB/key event flags used by app |
| 1858 | 0x742 | 2 | 0x2 | support byte; app checks bit 1 for MyFanboost2 |
| 1859 | 0x743 | 0 | 0x0 | fan customize L1 PWM source |
| 1860 | 0x744 | 30720 | 0x7800 | fan customize L2 PWM source |
| 1861 | 0x745 | 35960 | 0x8C78 | fan customize L3 PWM source |
| 1862 | 0x746 | 35980 | 0x8C8C | fan customize L4 PWM source |
| 1863 | 0x747 | 140 | 0x8C | fan customize L5 PWM source |
| 1873 | 0x751 | 27808 | 0x6CA0 | main fan control byte address |
| 1885 | 0x75D | 4608 | 0x1200 | trigger byte 2 |
| 1893 | 0x765 | 65535 | 0xFFFF | support byte 1 |
| 1894 | 0x766 | 255 | 0xFF | support byte 2 |
| 1895 | 0x767 | 512 | 0x200 | trigger byte |
| 1896 | 0x768 | 2 | 0x2 | status byte |
| 1922 | 0x782 | 0 | 0x0 | BIOS OEM byte 2 |

## Fan control mapping found

From `GamingCenter/GamingCenter/FanManagementPage.cs`:

- Normal/smart mode writes `WMIWriteECRAM(1873, 0)`.
- Fan boost/cold mode writes `WMIWriteECRAM(1873, 64)`.
- Custom basic level writes `WMIWriteECRAM(1873, 128 + level)` where level is 1..5.
- Custom advanced mode writes `WMIWriteECRAM(1873, 160)` and per-threshold PWM values:
  - L1: addr 1859
  - L2: addr 1860
  - L3: addr 1861
  - L4: addr 1862
  - L5: addr 1863

From `ECSpec.cs`:

- `ADDR_MAFAN_CONTROL_BYTE = 1873`
- modes: normal 0, boost 1, customize 2
- old flags: normal `0x00`, fanboost `0x40`, user fan `0x80`, user levels `0x81..0x85`.

## RGB/keyboard path

RGB keyboard code uses a mix of:

- EC/WMI bytes around 1857/1858;
- HID APIs (`HidD_*`, `HidP_*`, `SetupDiEnumDeviceInterfaces`, `CreateFile`);
- lighting models like `LM_ITE_RGB`, `ILM_RGBKB_SetEffectALL`, `HID_Set_Color_14H`, etc.

This needs a separate HID-device enumeration task. It is likely portable-ish on Windows/Linux if modeled as HID reports, but vendor-specific details must be mapped first.

## Registry state

The app stores settings under:

`HKLM\SOFTWARE\OEM\GamingCenter\...`

Examples: `MyFan3`, `RGBKeyboardView\Debug`, `LightBarOnOff`, fan mode/autosave values.

## Architecture recommendation after first pass

Pragmatic default: **.NET 10 + Avalonia UI + Windows service/helper**.

Why:

- The OEM app is already .NET/WPF and uses `System.Management`; porting WMI probes and registry code is straightforward.
- Avalonia gives a real cross-platform GUI with XAML/C#/MVVM and Windows/Linux support.
- ASP.NET Core/Kestrel can expose loopback HTTP/WebSocket for automation and named-pipe gRPC on Windows for local privileged IPC.
- Hardware control should live behind a small platform abstraction: `WindowsWmiEcBackend`, later `LinuxEc/HidBackend` if Linux control is possible.

Tauri/Rust remains viable if we want a web UI and Rust core, but Windows WMI + service + hardware interop is less direct and will need more glue. If we choose Rust, use `windows` crate for WMI/COM and `tauri::command` for UI IPC; still expose a local API from the Rust side.

## Safety boundaries

Next dynamic step should be explicit and reversible:

1. Read current fan mode byte.
2. Ask Rodrigo to toggle mode in the original UI.
3. Read byte again and diff.

Do **not** write EC address 1873 or PWM addresses until Rodrigo approves a controlled test window.
