param([string]$ExpectedVersion = '0.4.0')
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'WindowsComputerUse'
if ((Get-Content (Join-Path $base 'current.txt') -Raw).Trim() -ne $ExpectedVersion) {
    throw 'Install and start the expected release explicitly before running acceptance.'
}
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Net.Http
$bytes = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes((Join-Path $base 'data\token.dpapi')),
    $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [Convert]::ToBase64String($bytes)
[Array]::Clear($bytes, 0, $bytes.Length)
$client = [Net.Http.HttpClient]::new()
$client.BaseAddress = [Uri]'http://127.0.0.1:17842'
$client.Timeout = [TimeSpan]::FromSeconds(10)
$client.DefaultRequestHeaders.Authorization =
    [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$token = $null

function Api([string]$route, [string]$method = 'GET', $body = $null) {
    $content = $null
    $response = $null
    try {
        if ($method -eq 'GET') { $response = $client.GetAsync($route).GetAwaiter().GetResult() }
        else {
            $json = if ($null -eq $body) { '{}' } else { ConvertTo-Json -InputObject $body -Depth 8 -Compress }
            $content = [Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json')
            $response = $client.PostAsync($route, $content).GetAwaiter().GetResult()
        }
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "$route HTTP $([int]$response.StatusCode): $text" }
        return $text | ConvertFrom-Json
    } finally {
        if ($response) { $response.Dispose() }
        if ($content) { $content.Dispose() }
    }
}

function Until([scriptblock]$observe, [scriptblock]$condition, [string]$description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $value = & $observe
        if (& $condition $value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $description"
}

$testRoot = Join-Path $env:TEMP ('wcu-capability-' + [guid]::NewGuid().ToString('N'))
$process = $null
$handle = $null
New-Item -ItemType Directory $testRoot | Out-Null
try {
    $caps = Api '/v1/capabilities'
    if ($caps.apiVersion -ne 'v1' -or $caps.limits.maxKeySequence -lt 2 -or
        $caps.inputActions -notcontains 'key-sequence' -or $caps.inputActions -notcontains 'drag' -or
        $caps.limits.maxDragPoints -ne 128 -or $caps.limits.maxDragDurationMs -ne 5000 -or
        $caps.limits.maxDragSteps -ne 512 -or $caps.windowActions -notcontains 'close' -or
        $caps.hostVersion -notmatch ('^' + [regex]::Escape(($ExpectedVersion -split '-')[0]) + '(\.|$)')) {
        throw 'Capabilities/version contract mismatch (is the new host running?).'
    }
    # This is the only process/window the script acts on. Do not redirect input to other apps.
    $script = Join-Path $testRoot 'window.ps1'
    @'
Add-Type -AssemblyName PresentationFramework
$window = [Windows.Window]::new()
$window.Title = 'WCU Capability Validation ' + $PID
$window.Width = 450
$window.Height = 460
$window.WindowStartupLocation = 'CenterScreen'
$window.Topmost = $true
$panel = [Windows.Controls.StackPanel]::new()
$panel.Margin = '18'
$edit = [Windows.Controls.TextBox]::new()
$edit.Text = 'initial'
[Windows.Automation.AutomationProperties]::SetAutomationId($edit, 'CapabilityEdit')
$button = [Windows.Controls.Button]::new()
$button.Content = 'Pointer target'
$button.Height = 80
[Windows.Automation.AutomationProperties]::SetAutomationId($button, 'CapabilityButton')
$status = [Windows.Controls.TextBlock]::new()
$status.Text = 'Ready'
[Windows.Automation.AutomationProperties]::SetAutomationId($status, 'CapabilityStatus')
$button.Add_MouseEnter({ $status.Text = 'Moved' })
$button.Add_PreviewMouseLeftButtonDown({ $status.Text = 'Left:' + $_.ClickCount })
$button.Add_PreviewMouseRightButtonDown({ $status.Text = 'Right' })
$button.Add_PreviewMouseWheel({ $status.Text = 'Wheel:' + $_.Delta })
$panel.Children.Add($edit) | Out-Null
$panel.Children.Add($button) | Out-Null
$panel.Children.Add($status) | Out-Null
$canvas = [Windows.Controls.Canvas]::new()
$canvas.Height = 160
$canvas.Background = [Windows.Media.Brushes]::White
$canvas.Margin = '0,12,0,0'
$script:gestureNumber = 0
$script:gestureEvents = [Collections.Generic.List[object]]::new()
$script:stroke = $null
function Publish-GestureFile([string]$name, $value) {
    $path = Join-Path $PSScriptRoot $name
    [IO.File]::WriteAllText(($path + '.tmp'), (ConvertTo-Json -InputObject $value -Depth 8 -Compress))
    [IO.File]::Move(($path + '.tmp'), $path)
}
function Record-GestureEvent([string]$kind, $event) {
    $point = $event.GetPosition($canvas)
    $screen = $canvas.PointToScreen($point)
    $script:gestureEvents.Add([pscustomobject]@{
        kind = $kind; x = $screen.X; y = $screen.Y; left = $event.LeftButton.ToString() })
    if ($script:stroke) { $script:stroke.Points.Add($point) }
}
$canvas.Add_PreviewMouseLeftButtonDown({
    param($sender, $event)
    $script:gestureNumber++
    $script:gestureEvents.Clear()
    $script:stroke = [Windows.Shapes.Polyline]::new()
    $script:stroke.Stroke = [Windows.Media.Brushes]::Blue
    $script:stroke.StrokeThickness = 3
    $script:stroke.IsHitTestVisible = $false
    $canvas.Children.Add($script:stroke) | Out-Null
    Record-GestureEvent 'Down' $event
    $canvas.CaptureMouse() | Out-Null
})
$canvas.Add_PreviewMouseMove({
    param($sender, $event)
    if ($canvas.IsMouseCaptured -and $event.LeftButton -eq [Windows.Input.MouseButtonState]::Pressed) {
        Record-GestureEvent 'Move' $event
    }
})
$canvas.Add_PreviewMouseLeftButtonUp({
    param($sender, $event)
    if ($canvas.IsMouseCaptured) {
        Record-GestureEvent 'Up' $event
        $canvas.ReleaseMouseCapture()
        Publish-GestureFile ('gesture-' + $script:gestureNumber + '.json') @{
            events = @($script:gestureEvents.ToArray()); drawnPoints = $script:stroke.Points.Count }
        $script:stroke = $null
    }
})
$panel.Children.Add($canvas) | Out-Null
$window.Content = $panel
$window.Add_ContentRendered({
    $points = @(
        [Windows.Point]::new(25, 25),
        [Windows.Point]::new(($canvas.ActualWidth - 25), 25),
        [Windows.Point]::new(($canvas.ActualWidth - 25), ($canvas.ActualHeight - 25))
    ) | ForEach-Object {
        $screen = $canvas.PointToScreen($_)
        @{ x = [int][Math]::Round($screen.X); y = [int][Math]::Round($screen.Y) }
    }
    Publish-GestureFile 'gesture-ready.json' @{ points = @($points) }
})
$window.ShowDialog() | Out-Null
'@ | Set-Content -Path $script -Encoding UTF8
    $process = Start-Process powershell.exe -ArgumentList @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $script) -WorkingDirectory $env:SystemRoot -PassThru
    $window = Until { @((Api '/v1/windows') | Where-Object {
        $_.title -ceq ('WCU Capability Validation ' + $process.Id) -and $_.processId -eq $process.Id }) } {
        param($windows) @($windows | Where-Object { $null -ne $_ }).Count -eq 1
    } 'disposable WPF window'
    $handle = @($window)[0].handle
    $state = Api "/v1/windows/$handle/state"
    if ($state.title -cne ('WCU Capability Validation ' + $process.Id) -or -not $state.visible -or
        -not $state.enabled -or $state.minimized -or $state.handle -ne $handle) {
        throw 'Window state mismatch.'
    }
    $found = Api '/v1/elements/find' 'POST' @{
        handle = $handle; locator = @{ controlType = 'Button' }; maxResults = 20; maxNodes = 500; maxDepth = 10 }
    $target = @($found.elements | Where-Object { $_.state.automationId -ceq 'CapabilityButton' })
    if (-not $found.complete -or $target.Count -ne 1 -or -not $target[0].locatorUnique) {
        throw 'Find did not return a complete, uniquely locatable WPF button.'
    }
    $button = $target[0].state
    $locator = $target[0].locator
    $x = [int][Math]::Round($button.bounds.x + $button.bounds.width / 2)
    $y = [int][Math]::Round($button.bounds.y + $button.bounds.height / 2)
    function Read-Status { (Api '/v1/elements/inspect' 'POST' @{
        handle = $handle; locator = @{ automationId = 'CapabilityStatus' } }).name }
    $null = Api "/v1/windows/$handle/activate" 'POST'
    $null = Api '/v1/input/move' 'POST' @{ x = $x; y = $y }
    $null = Until { Read-Status } { param($value) $value -ceq 'Moved' } 'pointer move'
    $null = Api '/v1/input/pointer-click' 'POST' @{ x = $x; y = $y; button = 'left'; count = 2 }
    $null = Until { Read-Status } { param($value) $value -ceq 'Left:2' } 'double click'
    $null = Api '/v1/input/pointer-click' 'POST' @{ x = $x; y = $y; button = 'right'; count = 1 }
    $null = Until { Read-Status } { param($value) $value -ceq 'Right' } 'right click'
    $null = Api '/v1/input/scroll' 'POST' @{ delta = -120; x = $x; y = $y }
    $null = Until { Read-Status } { param($value) $value -ceq 'Wheel:-120' } 'wheel'
    $edit = @{ automationId = 'CapabilityEdit' }
    $null = Api '/v1/elements/focus' 'POST' @{ handle = $handle; locator = $edit }
    $null = Api '/v1/input/key-sequence' 'POST' @{ chords = @(
        @{ key = 65; modifiers = @(17) }, # Ctrl+A selects all
        @{ key = 90; modifiers = @() }, # Z
        @{ key = 66; modifiers = @(16) } # Shift+B
    ) }
    $null = Until { (Api '/v1/elements/inspect' 'POST' @{ handle = $handle; locator = $edit }).value } {
        param($value) $value -ceq 'zB'
    } 'ordered chords and key release'
    function Read-GestureFile([string]$name) {
        $path = Join-Path $testRoot $name
        if (Test-Path $path -PathType Leaf) { Get-Content $path -Raw | ConvertFrom-Json }
    }
    function Near-Point($observed, $expected) {
        [Math]::Abs($observed.x - $expected.x) -le 3 -and [Math]::Abs($observed.y - $expected.y) -le 3
    }
    $geometry = Until { Read-GestureFile 'gesture-ready.json' } { param($value) $null -ne $value } 'drawing surface geometry'
    $gestureReports = @()
    for ($gesture = 1; $gesture -le 2; $gesture++) {
        $points = if ($gesture -eq 1) { @($geometry.points[0], $geometry.points[1]) } else { @($geometry.points) }
        # Pacing gives WPF time to draw intermediate positions; immediate injection is covered by unit tests.
        $duration = 400
        $stepsPerSegment = 8
        $null = Api "/v1/windows/$handle/activate" 'POST'
        if (-not (Api "/v1/windows/$handle/state").foreground) { throw 'Disposable drawing window lost foreground.' }
        $drag = Api '/v1/input/drag' 'POST' @{
            points = $points; durationMs = $duration; stepsPerSegment = $stepsPerSegment }
        $expectedSteps = ($points.Count - 1) * $stepsPerSegment
        if (-not $drag.success -or $drag.metadata.steps -ne $expectedSteps -or
            $drag.metadata.emittedInputCount -ne ($expectedSteps + 3) -or
            $drag.metadata.elapsedMs -lt ($duration - 1)) { throw 'Drag timing/count contract mismatch.' }
        $observed = Until { Read-GestureFile ('gesture-' + $gesture + '.json') } {
            param($value) $null -ne $value
        } 'drawing surface Down/Move/Up sequence'
        $events = @($observed.events)
        $moves = @($events | Where-Object { $_.kind -ceq 'Move' })
        if ($events.Count -lt 3 -or $events[0].kind -cne 'Down' -or $events[-1].kind -cne 'Up' -or
            @($events | Where-Object { $_.kind -ceq 'Down' }).Count -ne 1 -or
            @($events | Where-Object { $_.kind -ceq 'Up' }).Count -ne 1 -or
            $events[0].left -cne 'Pressed' -or $events[-1].left -cne 'Released' -or
            $moves.Count -lt 1 -or @($moves | Where-Object { $_.left -cne 'Pressed' }).Count -ne 0 -or
            $observed.drawnPoints -lt 3 -or -not (Near-Point $events[0] $points[0]) -or
            -not (Near-Point $events[-1] $points[-1])) { throw 'Drawing surface did not observe a complete left-button gesture.' }
        if ($gesture -eq 2 -and @($moves | Where-Object { Near-Point $_ $points[1] }).Count -eq 0) {
            throw 'Drawing surface did not observe the stroke corner (input may have been coalesced or focus lost).'
        }
        $gestureReports += [pscustomobject]@{ Points = $points.Count; ObservedMoves = $moves.Count; Metadata = $drag.metadata }
    }
    $closed = Api "/v1/windows/$handle/close" 'POST'
    if (-not $closed.requestPosted -or $closed.handle -ne $handle) { throw 'WM_CLOSE not acknowledged.' }
    $null = Until { @((Api '/v1/windows') | Where-Object { $_.handle -eq $handle }) } {
        param($windows) @($windows | Where-Object { $null -ne $_ }).Count -eq 0
    } 'WM_CLOSE disappearance'
    [pscustomobject]@{ Version = $caps.hostVersion; Window = $handle; FindComplete = $found.complete;
        Chords = 'zB'; Gestures = $gestureReports; ClosePosted = $closed.requestPosted; CloseImmediatelyObserved = $closed.disappeared } |
        ConvertTo-Json -Depth 8 -Compress
} finally {
    # Cleanup is limited to the disposable process created by this script.
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    $client.Dispose()
    if (Test-Path $testRoot) { Remove-Item $testRoot -Recurse -Force }
}
