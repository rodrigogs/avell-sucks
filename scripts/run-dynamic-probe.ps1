<#
.SYNOPSIS
One-shot read-only dynamic probe for GamingCenter EC observation.

.NOTES
- Read-only. Do not write EC without explicit approval.
- Run from a Windows PowerShell / pwsh prompt with sufficient privilege.
.EXAMPLES
  # Interactive guided workflow
  .\run-dynamic-probe.ps1 -WorkDir .\probe-runs

  # Programmatic baseline + after + diff in one shot
  .\run-dynamic-probe.ps1 -WorkDir .\probe-runs -AutoBaseline -AutoAfter -AutoDiff -HumanAction "Set Performance: Fan Boost" -Label myrun
#>
param(
  [string]$WorkDir = ".probe",
  [switch]$AutoBaseline,
  [switch]$AutoAfter,
  [switch]$AutoDiff,
  [string]$HumanAction,
  [string]$Label,
  [string[]]$ExtraAddresses
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$snap = Join-Path $root 'ec-snapshot.ps1'

if (-not (Test-Path $snap)) {
  Write-Error "Expected helper at: $snap"
  exit 1
}

if (-not (Test-Path $WorkDir)) {
  New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
}

$stamp = if ($Label) { $Label } else { (Get-Date -Format 'yyyyMMdd-HHmmss') }
$baselineOut = Join-Path $WorkDir ("{0}-baseline.json" -f $stamp)
$afterOut    = Join-Path $WorkDir ("{0}-after.json"    -f $stamp)
$diffOut     = Join-Path $WorkDir ("{0}-diff.json"     -f $stamp)

function Invoke-Snap {
  param([string]$lbl, [string]$file, [string]$action)
  $argsList = @("-Label", $lbl, "-OutFile", $file)
  if ($action) { $argsList += @("-HumanAction", $action) }
  if ($ExtraAddresses) {
    $ExtraAddresses | ForEach-Object { $argsList += @("-ExtraAddresses", $_) }
  }
  Write-Host "`n==> Snapshot: $lbl"
  & $snap @argsList
  if (-not (Test-Path $file)) { Write-Error "Snapshot helper failed to write: $file"; exit 1 }
}

function Invoke-Diff {
  param([string]$before, [string]$after, [string]$out)
  $argsList = @("-Diff", $before, $after)
  if ($out) { $argsList += @("-OutFile", $out) }
  Write-Host "`n==> Diff: $before -> $after"
  & $snap @argsList
}

function Show-Summary {
  param([string]$file)
  if (-not (Test-Path $file)) { return }
  $obj = Get-Content -Raw $file | ConvertFrom-Json
  Write-Host ("`n=== {0} ===" -f $file)
  Write-Host ("Label       : {0}" -f $obj.Label)
  Write-Host ("HumanAction : {0}" -f $obj.HumanAction)
  Write-Host ("Timestamp   : {0}" -f $obj.Timestamp)
  Write-Host ("Readings    : {0}" -f $obj.Readings.Count)
  foreach ($r in $obj.Readings) {
    if ($r.Error) {
      Write-Host ("  {0,5} {1}  ERROR: {2}" -f $r.Address, $r.Hex, $r.Error)
    } else {
      Write-Host ("  {0,5} {1}  {2,6}  {3}" -f $r.Address, $r.Hex, $r.Value, $r.ValueHex)
    }
  }
}

if ($AutoBaseline) {
  Invoke-Snap -lbl ("{0}-baseline" -f $stamp) -file $baselineOut -action "baseline"
  Show-Summary -file $baselineOut
}

if ($AutoAfter) {
  if (-not $AutoBaseline -and -not (Test-Path $baselineOut)) {
    Write-Error "Run with -AutoBaseline first, or manually provide -HumanAction when capturing 'after'."
    exit 1
  }
  if (-not $HumanAction) {
    $HumanAction = Read-Host "Describe the UI action you just performed (e.g. Set Performance: Fan Boost)"
  }
  Invoke-Snap -lbl ("{0}-after" -f $stamp) -file $afterOut -action $HumanAction
  Show-Summary -file $afterOut
}

if ($AutoDiff) {
  if (-not (Test-Path $baselineOut) -or -not (Test-Path $afterOut)) {
    Write-Error "Both baseline and after snapshots must exist before diff."
    exit 1
  }
  Invoke-Diff -before $baselineOut -after $afterOut -out $diffOut
  if (Test-Path $diffOut) {
    $d = Get-Content -Raw $diffOut | ConvertFrom-Json
    Write-Host ("`n=== Diff summary ({0} changed) ===" -f $d.ChangedCount)
    foreach ($c in $d.Changes) {
      Write-Host ("  {0,5} {1}  {2}  ->  {3}  delta={4}" -f $c.Address, $c.Hex, $c.Before, $c.After, $c.Delta)
    }
  }
}

if (-not $AutoBaseline -and -not $AutoAfter -and -not $AutoDiff) {
  Write-Host @"
Dynamic probe - guided workflow

Run A: baseline
  $snap -Label baseline -OutFile "$baselineOut"

Run B: after Rodrigo toggles UI
  $snap -Label after -HumanAction "<describe action>" -OutFile "$afterOut"

Diff:
  $snap -Diff "$baselineOut" "$afterOut" -OutFile "$diffOut"

Wider scan:
  $snap -Label wide -ExtraAddresses 0x800,0x801,0x802 -OutFile "$WorkDir\wide.json"
"@
}
