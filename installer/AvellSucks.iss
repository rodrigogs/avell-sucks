; ============================================================================
;  AvellSucks — Inno Setup installer (per-machine)
;
;  Installs the self-contained win-x64 publish into Program Files, machine-wide,
;  with a Start-menu shortcut and a clean uninstaller. The app itself runs
;  elevated (requireAdministrator) and manages its own "start with Windows"
;  scheduled task at runtime, so this installer does NOT touch autostart — it only
;  offers to remove that task on uninstall so nothing is left pointing at a
;  deleted exe.
;
;  CI passes the real values in:  ISCC.exe AvellSucks.iss
;      /DMyAppVersion=1.2.3  /DPublishDir=<abs path>  /DOutputDir=<abs path>
;  The #ifndef defaults let the script also compile standalone for local testing.
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PublishDir
  ; Default assumes a local `dotnet publish ... -o dist/publish` from repo root.
  #define PublishDir "..\dist\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "AvellSucks"
#define MyAppExe "AvellSucks.exe"
#define MyAppPublisher "Rodrigo Gomes"
#define MyAppUrl "https://github.com/rodrigogs/avell-sucks"

[Setup]
; AppId is the permanent identity used for upgrade-in-place and Add/Remove.
; Generated once — never change it. The doubled { escapes a literal brace.
AppId={{4EBAFD39-21F1-4FC5-9FD1-B7DC092F760D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}/releases

; Per-machine: Program Files + HKLM. Requires elevation (the app needs it anyway).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}

; x64compatible covers native x64 and Windows-on-ARM x64 emulation, and forces
; the native 64-bit Program Files + registry view.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; The app is a tray app that ignores WM_CLOSE, so Inno's Restart Manager can't
; close it gracefully — we kill it ourselves in PrepareToInstall instead.
CloseApplications=no

OutputDir={#OutputDir}
; MUST stay constant across releases — the in-app updater matches this asset by
; exact name (AvellSucks-Setup.exe).
OutputBaseFilename=AvellSucks-Setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
; The entire self-contained publish folder (app + .NET runtime + pt-BR satellite).
; ignoreversion so runtime DLLs are always overwritten on update (their file
; versions don't bump between our releases); recursesubdirs for the satellite dir.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; After an interactive install, offer to launch.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; After a SILENT install (the in-app auto-update path), relaunch automatically.
Filename: "{app}\{#MyAppExe}"; Flags: nowait skipifnotsilent

[UninstallRun]
; Remove the "start with Windows" scheduled task the app may have created, so it
; doesn't linger pointing at a deleted exe. Runs before files are removed.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""AvellSucks Autostart"" /F"; \
  Flags: runhidden; RunOnceId: "DelAutostartTask"

[Code]
{ Kill any running instance before copying files: a self-contained app holds its
  own DLLs/driver locked while running, and the tray app ignores WM_CLOSE. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "Get-Process AvellSucks -ErrorAction SilentlyContinue | Stop-Process -Force"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
