$names = @('GamingCenter','GamingCenterTray','LaunchServGM','XtuService','OSDTpDetect','OOBEI2CTpOnOffDetect')
$result = @()
foreach ($name in $names) {
  $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
  foreach ($p in $procs) {
    $mods = @()
    try { $mods = $p.Modules | ForEach-Object { [pscustomobject]@{ ModuleName=$_.ModuleName; FileName=$_.FileName; FileVersion=$_.FileVersionInfo.FileVersion; ProductVersion=$_.FileVersionInfo.ProductVersion } } } catch { $mods = @([pscustomobject]@{ Error=$_.Exception.Message }) }
    $st = $null
    try { $st = $p.StartTime.ToString('o') } catch { $st = $null }
    $result += [pscustomobject]@{
      Name=$p.ProcessName
      Id=$p.Id
      Path=$p.Path
      StartTime=$st
      MainWindowTitle=$p.MainWindowTitle
      Modules=$mods
    }
  }
}
$result | ConvertTo-Json -Depth 6
