<#
.SYNOPSIS
Safe read-only EC snapshot helper for GamingCenter RE.
Captures labeled before/after snapshots of known EC/WMI addresses.

.NOTES
- Read-only. Do NOT use without approval.
- Pass -HumanAction to the second run for consistent labeling.
.EXAMPLES
  # Step 1: baseline
  ./ec-snapshot.ps1 -Label baseline -OutFile .\snapshots\before.json

  # Step 2: AFTER you toggle a UI control, capture the changed state
  ./ec-snapshot.ps1 -Label after -HumanAction "Set Performance: Fan Boost" -OutFile .\snapshots\after.json

  # Diff across two snapshots
  ./ec-snapshot.ps1 -Diff .\snapshots\before.json .\snapshots\after.json -OutFile .\snapshots\diff.json
#>
param(
  [switch]$Diff,
  [string]$DiffBefore,
  [string]$DiffAfter,
  [string]$Label = (Get-Date -Format 'yyyyMMdd-HHmmss'),
  [string]$HumanAction,
  [string]$OutFile,
  [string[]]$ExtraAddresses
)

$ErrorActionPreference = 'Stop'

function Invoke-Snapshot {
  param([string]$label, [string]$humanAction, [int[]]$addressList)
  $obj = New-Object System.Management.ManagementObject('root\WMI', "AcpiTest_MULong.InstanceName='ACPI\PNP0C14\1_1'", $null)
  $result = [ordered]@{
    Schema         = 'ec-wmi-readonly-snapshot'
    Timestamp      = Get-Date -Format 'o'
    Label          = $label
    HumanAction    = $humanAction
    Source         = 'AcpiTest_MULong.InstanceName=ACPI\PNP0C14\1_1'
    AddressWidth   = 'ulong-encoded'
    Encoding       = 'Data = 1099511627776 + addr'
    Notes          = 'Read-only. Do not write without approval.'
    Readings       = @()
  }
  $readings = @()
  foreach ($addr in $addressList) {
    try {
      $params = $obj.GetMethodParameters('GetSetULong')
      $params['Data'] = [uint64](1099511627776 + [uint64]$addr)
      $ret = $obj.InvokeMethod('GetSetULong', $params, $null)
      $v = [uint64]$ret['Return']
      $readings += [pscustomobject]@{
        Address  = [int]$addr
        Hex      = ('0x{0:X}' -f $addr)
        Value    = [uint64]$v
        ValueHex = ('0x{0:X}' -f $v)
      }
    } catch {
      $readings += [pscustomobject]@{
        Address  = [int]$addr
        Hex      = ('0x{0:X}' -f $addr)
        Value    = $null
        ValueHex = $null
        Error    = $_.Exception.Message
      }
    }
  }
  $result.Readings = $readings
  return $result
}

$DEFAULT_ADDRESSES = @(1857,1858,1859,1860,1861,1862,1863,1873,1885,1893,1894,1895,1896,1922)
$all = if ($ExtraAddresses) { $DEFAULT_ADDRESSES + $ExtraAddresses } else { $DEFAULT_ADDRESSES }

if ($Diff) {
  if (-not $DiffBefore -or -not $DiffAfter) {
    Write-Error "Use -DiffBefore and -DiffAfter with -Diff."
    exit 1
  }
  $before = Get-Content -Raw $DiffBefore | ConvertFrom-Json
  $after  = Get-Content -Raw $DiffAfter  | ConvertFrom-Json
  $map = @{}
  foreach ($r in $before.Readings) { $map[$r.Address] = $r }
  $diffs = @()
  foreach ($r in $after.Readings) {
    $b = $map[$r.Address]
    $changed = $false
    if (-not $b) { $changed = $true; $bFallback = 'MISSING-OR-NEW' } else { $bFallback = ('{0} ({1})' -f $b.ValueHex,$b.Value) }
    if ($b -and $r.Value -ne $b.Value) { $changed = $true }
    if ($changed) {
      $diffs += [pscustomobject]@{
        Address  = $r.Address
        Hex      = $r.Hex
        Before   = $bFallback
        After    = if ($r.Error) { 'ERROR: ' + $r.Error } else { ('{0} ({1})' -f $r.ValueHex,$r.Value) }
        Delta    = if ($b -and -not $r.Error -and $b.Value -ne $null -and $r.Value -ne $null) { ([long]$r.Value - [long]$b.Value) } else { $null }
      }
    }
  }
  $out = [ordered]@{
    DiffAt        = Get-Date -Format 'o'
    BeforeFile    = $DiffBefore
    AfterFile     = $DiffAfter
    ChangedCount  = $diffs.Count
    Changes       = $diffs
  }
  $json = $out | ConvertTo-Json -Depth 4
  if ($OutFile) {
    $parent = Split-Path -Parent $OutFile
    if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $json | Out-File -FilePath $OutFile -Encoding utf8
    Write-Host "Diff saved: $OutFile"
  } else {
    $json
  }
  return
}

if (-not $OutFile) {
  Write-Error "Pass -OutFile .\snapshots\<name>.json to record, or use -Diff ... to compare."
  exit 1
}
$parent = Split-Path -Parent $OutFile
if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

Invoke-Snapshot -label $Label -humanAction $HumanAction -addressList $all | ConvertTo-Json -Depth 4 | Out-File -FilePath $OutFile -Encoding utf8

Write-Host "Snapshot saved: $OutFile"
