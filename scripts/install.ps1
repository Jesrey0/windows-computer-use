param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$PackagePath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $PackagePath) { $PackagePath = Join-Path $root "artifacts/publish/$Version" }
$base = Join-Path $env:LOCALAPPDATA 'WindowsComputerUse'
$releases = Join-Path $base 'releases'
$release = Join-Path $releases $Version
$pointer = Join-Path $base 'current.txt'
foreach ($file in @('host/WindowsComputerUse.Host.exe', 'cli/WindowsComputerUse.Cli.exe')) {
    if (-not (Test-Path (Join-Path $PackagePath $file) -PathType Leaf)) { throw "Missing published file: $file" }
}
if (Test-Path $release) { throw "Release already installed: $Version" }
New-Item -ItemType Directory -Path $releases -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $base 'data') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $base 'logs') -Force | Out-Null
$stage = Join-Path $releases ('.stage-' + [guid]::NewGuid().ToString('N'))
$promoted = $false
$switched = $false
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item (Join-Path $PackagePath 'host') (Join-Path $stage 'host') -Recurse
    Copy-Item (Join-Path $PackagePath 'cli') (Join-Path $stage 'cli') -Recurse
    Move-Item $stage $release
    $promoted = $true
    # Launchers are stable and do not depend on shell cwd. Existing pointer is not changed
    # until the complete release and both launchers are in place.
    $hostLauncher = '@echo off' + "`r`n" + 'for /f "usebackq delims=" %%v in ("%~dp0current.txt") do set "WCU_VERSION=%%v"' + "`r`n" + '"%~dp0releases\%WCU_VERSION%\host\WindowsComputerUse.Host.exe" %*' + "`r`n"
    $cliLauncher = '@echo off' + "`r`n" + 'for /f "usebackq delims=" %%v in ("%~dp0current.txt") do set "WCU_VERSION=%%v"' + "`r`n" + '"%~dp0releases\%WCU_VERSION%\cli\WindowsComputerUse.Cli.exe" %*' + "`r`n"
    [IO.File]::WriteAllText((Join-Path $base 'wcu-host.cmd'), $hostLauncher)
    [IO.File]::WriteAllText((Join-Path $base 'wcu.cmd'), $cliLauncher)
    $temp = Join-Path $base ('.current-' + [guid]::NewGuid().ToString('N'))
    $backup = Join-Path $base ('.previous-' + [guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($temp, "$Version`r`n")
        if ([IO.File]::Exists($pointer)) { [IO.File]::Replace($temp, $pointer, $backup) }
        else { [IO.File]::Move($temp, $pointer) }
        $switched = $true
    } finally {
        if (Test-Path $temp) { Remove-Item $temp -Force }
        if (Test-Path $backup) { Remove-Item $backup -Force -ErrorAction SilentlyContinue }
    }
} catch {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    if ($promoted -and -not $switched) { Remove-Item $release -Recurse -Force }
    throw
}
Write-Host "Installed $Version at $base (restart a running host to activate)"
