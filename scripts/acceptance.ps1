param(
    [string]$ExpectedVersion = '0.2.3',
    [switch]$KeepArtifacts
)
$ErrorActionPreference = 'Stop'

$base = Join-Path $env:LOCALAPPDATA 'WindowsComputerUse'
$currentPath = Join-Path $base 'current.txt'
if (-not (Test-Path $currentPath -PathType Leaf)) { throw 'Windows Computer Use is not installed.' }
$current = (Get-Content $currentPath -Raw).Trim()
if ($current -ne $ExpectedVersion) { throw "Expected current version $ExpectedVersion, found $current." }

$hostLauncher = Join-Path $base 'wcu-host.cmd'
$cliLauncher = Join-Path $base 'wcu.cmd'
$tokenPath = Join-Path $base 'data\token.dpapi'
foreach ($path in @($hostLauncher, $cliLauncher, $tokenPath)) {
    if (-not (Test-Path $path -PathType Leaf)) { throw "Missing installed file: $path" }
}

# cmd.exe cannot use a UNC working directory. Launcher calls must originate from a Windows-local cwd.
Set-Location $env:SystemRoot

$testRoot = Join-Path $env:TEMP ('wcu-acceptance-' + [guid]::NewGuid().ToString('N'))
$wpfScript = Join-Path $testRoot 'validation-window.ps1'
$windowProcess = $null
$hostProcess = $null

function Stop-WcuHost {
    Get-Process WindowsComputerUse.Host -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Wait-Health([int]$Seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try {
            $health = Invoke-RestMethod -Uri 'http://127.0.0.1:17842/health' -TimeoutSec 1
            if ($health.status -eq 'ok') { return $health }
        } catch {}
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Installed host did not become healthy.'
}

function Get-Windows {
    $json = & $cliLauncher windows
    if ($LASTEXITCODE -ne 0) { throw 'Installed CLI windows command failed.' }
    return @((($json -join "`n") | ConvertFrom-Json))
}

function Find-Node($node, [string]$automationId) {
    if ($node.automationId -eq $automationId) { return $node }
    foreach ($child in @($node.children)) {
        $found = Find-Node $child $automationId
        if ($null -ne $found) { return $found }
    }
    return $null
}

function Wait-WindowTitle([string]$pattern, [int]$Seconds = 5) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $match = Get-Windows | Where-Object { $_.title -like $pattern } | Select-Object -First 1
        if ($match) { return $match }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Window title did not match '$pattern'."
}

New-Item -ItemType Directory -Path $testRoot | Out-Null
@'
Add-Type -AssemblyName PresentationFramework
$window = New-Object System.Windows.Window
$window.Title = 'WCU Validation'
$window.Width = 440
$window.Height = 220
$window.WindowStartupLocation = 'CenterScreen'
$window.Topmost = $true
$window.Add_ContentRendered({ $window.Activate() | Out-Null; $window.Focus() | Out-Null })
$panel = New-Object System.Windows.Controls.StackPanel
$panel.Margin = '18'

$text = New-Object System.Windows.Controls.TextBox
$text.Text = 'initial'
$text.Margin = '0,0,0,12'
$text.FontSize = 18
[System.Windows.Automation.AutomationProperties]::SetAutomationId($text, 'ValidationText')
$text.Add_TextChanged({ $window.Title = 'WCU Validation Text=' + $text.Text })

$button = New-Object System.Windows.Controls.Button
$button.Content = 'Invoke Me'
$button.Height = 42
[System.Windows.Automation.AutomationProperties]::SetAutomationId($button, 'ValidationButton')
$button.Add_Click({ $window.Title = 'WCU Validation Invoked' })

$panel.Children.Add($text) | Out-Null
$panel.Children.Add($button) | Out-Null
$window.Content = $panel
$window.ShowDialog() | Out-Null
'@ | Set-Content -Path $wpfScript -Encoding UTF8

try {
    Stop-WcuHost
    Start-Sleep -Milliseconds 250

    $hostProcess = Start-Process -FilePath $hostLauncher -WindowStyle Hidden -PassThru
    $health = Wait-Health 30

    $listener = Get-NetTCPConnection -LocalPort 17842 -State Listen -ErrorAction Stop | Select-Object -First 1
    if ($listener.LocalAddress -ne '127.0.0.1') {
        throw "Host listener is not IPv4 loopback-only: $($listener.LocalAddress)"
    }

    $unauthorized = $null
    try {
        Invoke-WebRequest -Uri 'http://127.0.0.1:17842/v1/windows' -UseBasicParsing -TimeoutSec 2 | Out-Null
        $unauthorized = 200
    } catch {
        if ($_.Exception.Response) { $unauthorized = [int]$_.Exception.Response.StatusCode }
    }
    if ($unauthorized -ne 401) { throw "Expected unauthenticated /v1/windows to return 401, got $unauthorized." }

    Add-Type -AssemblyName System.Security
    $encrypted = [IO.File]::ReadAllBytes($tokenPath)
    $tokenBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $token = [Convert]::ToBase64String($tokenBytes)
    $headers = @{ Authorization = "Bearer $token" }

    $windowProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $wpfScript
    ) -PassThru
    $window = Wait-WindowTitle 'WCU Validation*' 10

    $treeJson = & $cliLauncher tree ([string]$window.handle)
    if ($LASTEXITCODE -ne 0) { throw 'Installed CLI tree command failed.' }
    $tree = ($treeJson -join "`n") | ConvertFrom-Json
    if (-not $tree.root -or $tree.nodeCount -lt 3) { throw "Unexpected UI tree node count: $($tree.nodeCount)" }

    $textNode = Find-Node $tree.root 'ValidationText'
    $buttonNode = Find-Node $tree.root 'ValidationButton'
    if (-not $textNode) { throw 'Validation TextBox was not exposed through UI Automation.' }
    if (-not $buttonNode) { throw 'Validation Button was not exposed through UI Automation.' }
    if (-not $textNode.canSetValue) { throw 'Validation TextBox does not expose ValuePattern.' }
    if (-not $buttonNode.canInvoke) { throw 'Validation Button does not expose InvokePattern.' }

    $setBody = @{
        handle = $window.handle
        path = @($textNode.path)
        value = 'changed-by-wcu'
    } | ConvertTo-Json -Depth 8
    $setResult = Invoke-RestMethod -Uri 'http://127.0.0.1:17842/v1/actions/set-value' -Method Post -Headers $headers -ContentType 'application/json' -Body $setBody -TimeoutSec 5
    if (-not $setResult.success) { throw 'ValuePattern action did not report success.' }
    $window = Wait-WindowTitle 'WCU Validation Text=changed-by-wcu' 5

    $invokeBody = @{
        handle = $window.handle
        path = @($buttonNode.path)
    } | ConvertTo-Json -Depth 8
    $invokeResult = Invoke-RestMethod -Uri 'http://127.0.0.1:17842/v1/actions/invoke' -Method Post -Headers $headers -ContentType 'application/json' -Body $invokeBody -TimeoutSec 5
    if (-not $invokeResult.success) { throw 'InvokePattern action did not report success.' }
    $window = Wait-WindowTitle 'WCU Validation Invoked' 5

    $treeJson = & $cliLauncher tree ([string]$window.handle)
    $tree = ($treeJson -join "`n") | ConvertFrom-Json
    $textNode = Find-Node $tree.root 'ValidationText'
    if (-not $textNode) { throw 'Validation TextBox disappeared before input fallback test.' }

    $prepBody = @{
        handle = $window.handle
        path = @($textNode.path)
        value = 'input:'
    } | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Uri 'http://127.0.0.1:17842/v1/actions/set-value' -Method Post -Headers $headers -ContentType 'application/json' -Body $prepBody -TimeoutSec 5 | Out-Null
    $window = Wait-WindowTitle 'WCU Validation Text=input:' 5

    $x = [int][Math]::Round($textNode.bounds.x + ($textNode.bounds.width / 2))
    $y = [int][Math]::Round($textNode.bounds.y + ($textNode.bounds.height / 2))
    $clickBody = @{ x = $x; y = $y } | ConvertTo-Json
    Invoke-RestMethod -Uri 'http://127.0.0.1:17842/v1/input/click' -Method Post -Headers $headers -ContentType 'application/json' -Body $clickBody -TimeoutSec 5 | Out-Null
    Start-Sleep -Milliseconds 200
    $keysBody = @{ text = 'xyz'; virtualKey = $null } | ConvertTo-Json
    Invoke-RestMethod -Uri 'http://127.0.0.1:17842/v1/input/keys' -Method Post -Headers $headers -ContentType 'application/json' -Body $keysBody -TimeoutSec 5 | Out-Null
    $window = Wait-WindowTitle 'WCU Validation Text=input:*xyz*' 5

    $windowPng = Join-Path $testRoot 'window.png'
    & $cliLauncher screenshot ([string]$window.handle) $windowPng | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $windowPng -PathType Leaf)) { throw 'Window screenshot failed.' }
    $windowBytes = [IO.File]::ReadAllBytes($windowPng)
    if ($windowBytes.Length -lt 100 -or $windowBytes[0] -ne 137 -or $windowBytes[1] -ne 80 -or
        $windowBytes[2] -ne 78 -or $windowBytes[3] -ne 71) { throw 'Window screenshot is not a PNG.' }

    $desktopPng = Join-Path $testRoot 'desktop.png'
    Invoke-WebRequest -Uri 'http://127.0.0.1:17842/v1/desktop/screenshot' -Headers $headers -OutFile $desktopPng -UseBasicParsing -TimeoutSec 15
    $desktopBytes = [IO.File]::ReadAllBytes($desktopPng)
    if ($desktopBytes.Length -lt 100 -or $desktopBytes[0] -ne 137 -or $desktopBytes[1] -ne 80 -or
        $desktopBytes[2] -ne 78 -or $desktopBytes[3] -ne 71) { throw 'Desktop screenshot is not a PNG.' }

    [pscustomobject]@{
        Version = $current
        Health = $health.status
        Listener = $listener.LocalAddress + ':' + $listener.LocalPort
        UnauthorizedStatus = $unauthorized
        WindowHandle = $window.handle
        TreeNodes = $tree.nodeCount
        ValuePattern = $true
        InvokePattern = $true
        SendInput = $true
        WindowScreenshotBytes = $windowBytes.Length
        DesktopScreenshotBytes = $desktopBytes.Length
    } | ConvertTo-Json -Compress
}
finally {
    if ($windowProcess -and -not $windowProcess.HasExited) {
        Stop-Process -Id $windowProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Get-Process powershell -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -like 'WCU Validation*' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-WcuHost
    Start-Sleep -Milliseconds 250
    if (-not $KeepArtifacts -and (Test-Path $testRoot)) {
        Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
