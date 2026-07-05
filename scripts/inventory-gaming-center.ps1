$ErrorActionPreference = 'Continue'
$patterns = 'Gaming|Game|Center|Control|Hotkey|LED|RGB|Fan|Clevo|OEM|Power|Keyboard|Backlight|CCC|CC|Flexikey'

function SafeSelectProcess($proc) {
  [pscustomobject]@{
    ProcessId = $proc.ProcessId
    ParentProcessId = $proc.ParentProcessId
    Name = $proc.Name
    ExecutablePath = $proc.ExecutablePath
    CommandLine = $proc.CommandLine
    CreationDate = $proc.CreationDate
  }
}

$processes = Get-CimInstance Win32_Process |
  Where-Object { ($_.Name -match $patterns) -or ($_.CommandLine -match $patterns) -or ($_.ExecutablePath -match $patterns) } |
  ForEach-Object { SafeSelectProcess $_ }

$services = Get-CimInstance Win32_Service |
  Where-Object { ($_.Name -match $patterns) -or ($_.DisplayName -match $patterns) -or ($_.PathName -match $patterns) } |
  Select-Object Name,DisplayName,State,StartMode,ProcessId,PathName,Description

$startup = Get-CimInstance Win32_StartupCommand |
  Where-Object { ($_.Name -match $patterns) -or ($_.Command -match $patterns) -or ($_.Location -match $patterns) } |
  Select-Object Name,Command,Location,User

$listeners = @()
try {
  $listeners = Get-NetTCPConnection -State Listen | ForEach-Object {
    $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
    [pscustomobject]@{ Proto='TCP'; LocalAddress=$_.LocalAddress; LocalPort=$_.LocalPort; PID=$_.OwningProcess; Process=$p.ProcessName; Path=$p.Path }
  } | Where-Object { ($_.Process -match $patterns) -or ($_.Path -match $patterns) }
} catch {}

$drivers = Get-CimInstance Win32_SystemDriver |
  Where-Object { ($_.Name -match $patterns) -or ($_.DisplayName -match $patterns) -or ($_.PathName -match $patterns) -or ($_.Description -match $patterns) } |
  Select-Object Name,DisplayName,State,StartMode,PathName,Description

$installed = @()
$uninstallRoots = @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
foreach ($root in $uninstallRoots) {
  $installed += Get-ItemProperty $root -ErrorAction SilentlyContinue |
    Where-Object { ($_.DisplayName -match $patterns) -or ($_.InstallLocation -match $patterns) -or ($_.Publisher -match 'Clevo|OEM|Tongfang|Intel|Control') } |
    Select-Object DisplayName,DisplayVersion,Publisher,InstallLocation,InstallSource,UninstallString,QuietUninstallString,PSPath
}

$windows = @()
try {
  Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win32Enum {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
"@ -ErrorAction SilentlyContinue
  [Win32Enum]::EnumWindows({ param($hWnd, $lParam)
    if ([Win32Enum]::IsWindowVisible($hWnd)) {
      $sb = New-Object System.Text.StringBuilder 512
      [void][Win32Enum]::GetWindowText($hWnd, $sb, $sb.Capacity)
      $title = $sb.ToString()
      $pid = 0
      [void][Win32Enum]::GetWindowThreadProcessId($hWnd, [ref]$pid)
      $p = Get-Process -Id $pid -ErrorAction SilentlyContinue
      if ($title -match $patterns -or $p.ProcessName -match $patterns -or $p.Path -match $patterns) {
        $script:windows += [pscustomobject]@{ Handle=$hWnd.ToString(); PID=$pid; Process=$p.ProcessName; Path=$p.Path; Title=$title }
      }
    }
    return $true
  }, [IntPtr]::Zero) | Out-Null
} catch {}

$result = [ordered]@{
  Timestamp = (Get-Date).ToString('o')
  ComputerName = $env:COMPUTERNAME
  Processes = $processes
  Services = $services
  StartupCommands = $startup
  Listeners = $listeners
  Drivers = $drivers
  InstalledPrograms = $installed
  Windows = $windows
}

$result | ConvertTo-Json -Depth 8
