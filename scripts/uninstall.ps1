param([switch]$KeepData)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'WindowsComputerUse'
if (-not (Test-Path $base)) { return }
# A running host may lock a release. Stop it before uninstalling; do not kill arbitrary processes.
$releases = Join-Path $base 'releases'
if (Test-Path $releases) { Remove-Item $releases -Recurse -Force }
foreach ($name in @('current.txt', 'wcu.cmd', 'wcu-host.cmd')) {
    $file = Join-Path $base $name
    if (Test-Path $file) { Remove-Item $file -Force }
}
if (-not $KeepData) {
    foreach ($name in @('data', 'logs')) {
        $folder = Join-Path $base $name
        if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    }
}
if (-not (Get-ChildItem $base -Force)) { Remove-Item $base -Force }
Write-Host "Uninstalled WindowsComputerUse (KeepData: $KeepData)"
