param([int]$AppProcessId, [string]$OnlineBvid)
$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$output = Join-Path $PSScriptRoot '../artifacts/ui/progress-regression'
$checks = 0
$sampleIndex = 0
function Snapshot([hashtable]$request = @{}) {
    if ($AppProcessId) { $request.AppProcessId = $AppProcessId }
    $script:sampleIndex++
    $samplePath = "$output-$script:sampleIndex"
    & $inspect @request -OutputPath $samplePath | Out-Null
    Get-Content -LiteralPath "$samplePath.json" -Raw | ConvertFrom-Json -AsHashtable
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Slider($snapshot) {
    @($snapshot.snapshot.nodes | Where-Object { $_.automationId -eq 'ProgressSlider' })[0]
}
function Fill($snapshot) {
    # Native template descendants follow the slider in the measured visual tree.
    $track = Slider $snapshot
    $snapshot.snapshot.nodes | Where-Object {
        $_.name -eq 'HorizontalDecreaseRect' -and $_.id -gt $track.id
    } | Select-Object -First 1
}
function CheckAdvancing {
    $before = Snapshot
    Start-Sleep -Milliseconds 1500
    $after = Snapshot
    Check ($after.playback.position -gt $before.playback.position + .8) 'Playback position stopped advancing.'
    Check ((Slider $after).value -gt (Slider $before).value) 'The actual XAML slider stopped advancing.'
    Check ((Fill $after).bounds.width -gt (Fill $before).bounds.width) 'The visible progress fill stopped advancing.'
    Check ([Math]::Abs((Slider $after).value - $after.playback.progress) -lt .005) 'The slider disagrees with the playing position.'
    Check ($after.playback.engine.seeks -eq $before.playback.engine.seeks) 'Position notifications caused an unintended seek.'
    Check ($after.playback.engine.updates -notin @('Faulted', 'RanToCompletion', 'Canceled')) 'The native position update loop stopped.'
}
$initial = Snapshot
Check ($initial.playback.count -eq 0) 'Run with an empty queue; existing playback is preserved.'
$started = $false
try {
    $fixture = @{ PlaybackFixture = 'sample' }
    if ($OnlineBvid) { $fixture = @{ PlaybackFixture = 'online'; PlaybackBvid = $OnlineBvid } }
    $started = $true
    $null = Snapshot $fixture
    $layout = Snapshot
    $bar = $layout.snapshot.nodes | Where-Object { $_.name -eq 'TransportRoot' } | Select-Object -First 1
    Check ([Math]::Abs((Fill $layout).bounds.y + 2 - $bar.bounds.y) -lt 1) 'The seek track moved away from the bar upper edge.'
    CheckAdvancing
    $paused = Snapshot @{ PlaybackAction = 'pause' }
    Start-Sleep -Milliseconds 750
    $still = Snapshot
    Check ([Math]::Abs($still.playback.position - $paused.playback.position) -lt .1) 'Position continued advancing after pause.'
    $seeked = Snapshot @{ PlaybackProgress = .25 }
    Check ([Math]::Abs($seeked.playback.position - $seeked.playback.duration * .25) -lt .5) 'Paused seek did not reach the requested position.'
    $null = Snapshot @{ PlaybackAction = 'play' }
    CheckAdvancing
    $null = Snapshot @{ PlaybackAction = 'next' }
    CheckAdvancing
    $null = Snapshot @{ Tab = 'nowplaying' }
    CheckAdvancing
    $null = Snapshot @{ Tab = 'favorites' }
    CheckAdvancing
} finally {
    if ($started) {
        $restore = @{ PlaybackFixture = 'restore' }
        if ($initial.lifetime.activeTab) { $restore.Tab = $initial.lifetime.activeTab }
        $null = Snapshot $restore
    }
}
Write-Output "$checks native playback progress checks passed."
