param(
    [string]$ExpectedVersion = '0.3.0',
    [string]$CalculatorHandle
)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'WindowsComputerUse'
if ($ExpectedVersion) {
    $current = (Get-Content (Join-Path $base 'current.txt') -Raw).Trim()
    if ($current -ne $ExpectedVersion) { throw "Expected installed version $ExpectedVersion, got $current." }
}
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Net.Http
$tokenBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes((Join-Path $base 'data\token.dpapi')),
    $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [Convert]::ToBase64String($tokenBytes)
[Array]::Clear($tokenBytes, 0, $tokenBytes.Length)

$client = [System.Net.Http.HttpClient]::new()
$client.BaseAddress = [Uri]'http://127.0.0.1:17842'
$client.Timeout = [TimeSpan]::FromSeconds(10)
$client.DefaultRequestHeaders.Authorization =
    [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$token = $null

function Api([string]$route, [string]$method = 'GET', $body = $null) {
    $content = $null
    $response = $null
    try {
        if ($method -ceq 'GET') {
            $response = $client.GetAsync($route).GetAwaiter().GetResult()
        } elseif ($method -ceq 'POST') {
            $json = if ($null -eq $body) { '{}' } else {
                ConvertTo-Json -InputObject $body -Depth 8 -Compress
            }
            $content = [System.Net.Http.StringContent]::new(
                $json, [Text.Encoding]::UTF8, 'application/json')
            $response = $client.PostAsync($route, $content).GetAwaiter().GetResult()
        } else {
            throw "Unsupported acceptance HTTP method: $method"
        }
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "$method $route failed: HTTP $([int]$response.StatusCode) $text"
        }
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        return $text | ConvertFrom-Json
    } finally {
        if ($response) { $response.Dispose() }
        if ($content) { $content.Dispose() }
    }
}

$health = Api '/health'
if ($health.status -ne 'ok') {
    throw 'Start the new installed host in the interactive Windows user session first.'
}
if ($ExpectedVersion) {
    $assemblyVersion = [Regex]::Escape(($ExpectedVersion -split '-')[0])
    if ($health.version -notmatch "^$assemblyVersion(\.|$)") {
        throw "Running host version $($health.version) does not match expected $ExpectedVersion. Restart it."
    }
}

function Wait-Window([string]$title, [int]$seconds = 10) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $windows = Api '/v1/windows'
        $matches = @($windows.GetEnumerator() | Where-Object { $_.title -ceq $title })
        if ($matches.Count -eq 1) { return $matches[0] }
        if ($matches.Count -gt 1) { throw "Ambiguous window title: $title" }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "No window titled '$title' became available."
}

function Wait-State([string]$handle, $locator, [scriptblock]$predicate, [int]$seconds = 5) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $state = Api '/v1/elements/inspect' 'POST' @{ handle = $handle; locator = $locator }
        if (& $predicate $state) { return $state }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Element state did not reach the expected value before the deadline.'
}

function ById([string]$id) { return @{ automationId = $id } }

$testRoot = Join-Path $env:TEMP ('wcu-semantic-' + [guid]::NewGuid().ToString('N'))
$wpfProcess = $null
New-Item -ItemType Directory $testRoot | Out-Null
try {
    $wpfScript = Join-Path $testRoot 'validation-window.ps1'
    @'
Add-Type -AssemblyName PresentationFramework
$window = New-Object System.Windows.Window
$window.Title = 'WCU Semantic Validation'
$window.Width = 440
$window.Height = 220
$window.WindowStartupLocation = 'CenterScreen'
$window.Topmost = $true
$panel = New-Object System.Windows.Controls.StackPanel
$panel.Margin = '18'
$status = New-Object System.Windows.Controls.TextBlock
$status.Text = 'Ready'
[System.Windows.Automation.AutomationProperties]::SetAutomationId($status, 'ValidationStatus')
$text = New-Object System.Windows.Controls.TextBox
$text.Text = 'initial'
$text.Margin = '0,0,0,12'
[System.Windows.Automation.AutomationProperties]::SetAutomationId($text, 'ValidationText')
$text.Add_TextChanged({ $status.Text = 'Text=' + $text.Text })
$button = New-Object System.Windows.Controls.Button
$button.Content = 'Invoke Me'
$button.Height = 42
[System.Windows.Automation.AutomationProperties]::SetAutomationId($button, 'ValidationButton')
$button.Add_Click({ $status.Text = 'Invoked' })
$panel.Children.Add($text) | Out-Null
$panel.Children.Add($button) | Out-Null
$panel.Children.Add($status) | Out-Null
$window.Content = $panel
$window.ShowDialog() | Out-Null
'@ | Set-Content -Path $wpfScript -Encoding UTF8
    $wpfProcess = Start-Process powershell.exe -ArgumentList @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $wpfScript) -WorkingDirectory $env:SystemRoot -PassThru
    $window = Wait-Window 'WCU Semantic Validation'
    $handle = $window.handle
    # Scoped under the uniquely identified window root; no child path is stored.
    $edit = @{ automationId = 'ValidationText'; controlType = 'Edit'; ancestor = @{
        name = 'WCU Semantic Validation'; controlType = 'Window' } }
    $button = @{ automationId = 'ValidationButton'; controlType = 'Button' }
    $status = ById 'ValidationStatus'
    $before = Api '/v1/elements/inspect' 'POST' @{ handle = $handle; locator = $edit }
    if ($before.value -cne 'initial' -or -not $before.canSetValue) { throw 'WPF editor precondition failed.' }
    $activated = Api "/v1/windows/$handle/activate" 'POST'
    if (-not $activated.isForeground) { throw 'Activation not confirmed.' }
    $focused = Api '/v1/elements/focus' 'POST' @{ handle = $handle; locator = $edit }
    if (-not $focused.focused) { throw 'Element focus not confirmed.' }
    Api '/v1/actions/set-value-located' 'POST' @{
        handle = $handle; locator = $edit; value = 'semantic-readback' } | Out-Null
    $null = Wait-State $handle $edit { param($state) $state.value -ceq 'semantic-readback' }
    Api '/v1/actions/invoke-located' 'POST' @{ handle = $handle; locator = $button } | Out-Null
    $null = Wait-State $handle $status { param($state) $state.name -ceq 'Invoked' }

    # Calculator is an existing operator-opened window; its display is cleared and
    # digit 7 invoked. Localized titles require the explicit CalculatorHandle option.
    if (-not $CalculatorHandle) { $CalculatorHandle = (Wait-Window 'Calculator' 3).handle }
    $calc = $CalculatorHandle
    $clear = ById 'clearButton'
    $seven = ById 'num7Button'
    $display = ById 'CalculatorResults'
    $null = Api '/v1/elements/inspect' 'POST' @{ handle = $calc; locator = $seven }
    $null = Api "/v1/windows/$calc/activate" 'POST'
    Api '/v1/actions/invoke-located' 'POST' @{ handle = $calc; locator = $clear } | Out-Null
    $null = Wait-State $calc $display { param($state) $state.name -match '(?<![0-9])0(?![0-9])' }
    Api '/v1/actions/invoke-located' 'POST' @{ handle = $calc; locator = $seven } | Out-Null
    $result = Wait-State $calc $display { param($state) $state.name -match '7' }
    [pscustomobject]@{
        WpfWindow = $handle
        WpfFocus = $true
        WpfValue = 'semantic-readback'
        WpfInvoke = $true
        CalculatorWindow = $calc
        CalculatorDisplay = $result.name
    } | ConvertTo-Json -Compress
}
finally {
    if ($wpfProcess -and -not $wpfProcess.HasExited) {
        Stop-Process -Id $wpfProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $testRoot) { Remove-Item $testRoot -Recurse -Force }
}
