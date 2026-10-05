param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PackagePath
)
$ErrorActionPreference = 'Stop'
$pointer = Join-Path (Join-Path $env:LOCALAPPDATA 'WindowsComputerUse') 'current.txt'
if (-not (Test-Path $pointer -PathType Leaf)) { throw 'No existing installation; run install.ps1 first.' }
& (Join-Path $PSScriptRoot 'install.ps1') -Version $Version -PackagePath $PackagePath
if (-not $?) { throw 'Update failed.' }
