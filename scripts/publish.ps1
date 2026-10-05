param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '0.1.0',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root "artifacts/publish/$Version"
if (Test-Path $destination) { throw "Release already exists: $destination" }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
try {
    foreach ($project in @('Host', 'Cli')) {
        $name = "WindowsComputerUse.$project"
        $target = Join-Path $destination $project.ToLowerInvariant()
        & $Dotnet publish (Join-Path $root "src/$name/$name.csproj") -c Release -r win-x64 --self-contained true -p:Version=$Version -o $target
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }
    }
} catch {
    Remove-Item $destination -Recurse -Force
    throw
}
Write-Host "Published $destination"
