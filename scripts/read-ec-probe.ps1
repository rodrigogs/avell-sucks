$ErrorActionPreference = 'Stop'
$addresses = @(1857,1858,1859,1860,1861,1862,1863,1873,1885,1893,1894,1895,1896,1922)
$obj = New-Object System.Management.ManagementObject('root\WMI', "AcpiTest_MULong.InstanceName='ACPI\PNP0C14\1_1'", $null)
$out = foreach ($addr in $addresses) {
  try {
    $params = $obj.GetMethodParameters('GetSetULong')
    $params['Data'] = [uint64](1099511627776 + [uint64]$addr)
    $ret = $obj.InvokeMethod('GetSetULong', $params, $null)
    [pscustomobject]@{ Address=$addr; Hex=('0x{0:X}' -f $addr); Value=[uint64]$ret['Return']; ValueHex=('0x{0:X}' -f [uint64]$ret['Return']); Ok=$true; Error=$null }
  } catch {
    [pscustomobject]@{ Address=$addr; Hex=('0x{0:X}' -f $addr); Value=$null; ValueHex=$null; Ok=$false; Error=$_.Exception.Message }
  }
}
$out | ConvertTo-Json -Depth 4
