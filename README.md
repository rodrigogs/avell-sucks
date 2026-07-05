# Gaming Center replacement

Reverse-engineering workspace for the OEM GamingCenter app running on Rodrigo-Avell.

## Goal

Build a safer, modern replacement app to control this PC: fan/performance modes, keyboard/RGB/backlight, power-related toggles, and expose a local automation API.

## Safety rule

Until we explicitly approve it, investigation is read-only. Do not write EC/WMI values, call vendor setters, kill services, install drivers, or attach invasive debuggers to privileged processes without asking Rodrigo here.

## Current artifacts

- `inventory.json` — process/service/startup/driver/program inventory.
- `process-detail.json` — module inventory for GamingCenter processes.
- `ec-read-probe.json` — read-only WMI EC probe for key addresses.
- `analysis/*.strings.txt` and `analysis/strings-interesting.txt` — static string extraction.
- `scripts/*.ps1` — reproducible Windows inventory/probe scripts.
- Decompiled source: `C:\Users\rdp\RE-tools\gaming-center-decompiled`.
