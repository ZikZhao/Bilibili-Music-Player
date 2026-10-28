param([int]$AppProcessId)
$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$output = Join-Path $PSScriptRoot '../artifacts/ui/expanded-regression'
$checks = 0
$sampleIndex = 0
function Snapshot([hashtable]$request = @{}) {
    if ($AppProcessId) { $request.AppProcessId = $AppProcessId }
    $script:sampleIndex++
    $samplePath = "$output-$script:sampleIndex"
    & $inspect @request -OutputPath $samplePath | Out-Null
    $state = Get-Content -LiteralPath "$samplePath.json" -Raw | ConvertFrom-Json -AsHashtable
    for ($attempt = 0; $state.playerTransition.animating -and $attempt -lt 12; $attempt++) {
        Start-Sleep -Milliseconds 50
        $poll = @{ OutputPath = $samplePath }
        if ($AppProcessId) { $poll.AppProcessId = $AppProcessId }
        & $inspect @poll | Out-Null
        $state = Get-Content -LiteralPath "$samplePath.json" -Raw | ConvertFrom-Json -AsHashtable
    }
    Check (-not $state.playerTransition.animating) 'Player transition did not settle.'
    $state
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function WaitForBackground {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $state = Snapshot
        if ($state.playerBackground.ready -or $state.playerBackground.error) { return $state }
        Start-Sleep -Milliseconds 100
    }
    throw 'Cover decode did not complete.'
}
$initial = Snapshot
Check ($initial.playback.count -eq 0) 'Run with an empty queue; existing playback is preserved.'
$started = $false
try {
    $started = $true
    $fixture = Snapshot @{ PlaybackFixture = 'backdrop'; PlaybackAction = 'pause'; Tab = 'favorites' }
    $probe = Snapshot @{ ProbePlayerTransition = $true }
    $motion = $probe.transitionProbe
    if ($probe.playerTransition.enabled) {
        Check ($motion.opening -gt 0 -and $motion.opening -lt 1) 'Opening did not animate from the bottom.'
        Check ([Math]::Abs($motion.opening - $motion.closeStart) -lt .002) 'Reversing to close jumped to an endpoint.'
        Check ($motion.closing -lt $motion.closeStart -and $motion.closing -gt 0) 'Midway close did not travel down.'
        Check ([Math]::Abs($motion.closing - $motion.reopenStart) -lt .002) 'Reopening during close jumped to an endpoint.'
    }
    Check ($motion.reopened -eq 1 -and $motion.openVisible) 'A stale close completion hid the reopened player.'
    Check ($motion.closed -eq 0 -and $motion.closedHidden) 'The closed player remained visible.'
    Check ($probe.playback.sourceId -eq $fixture.playback.sourceId) 'Animation replaced the audio source.'
    $normal = Snapshot @{ Target = 'NowPlayingDetails'; State = 'Normal'; Focus = 'navigation' }
    $hover = Snapshot @{ Target = 'NowPlayingDetails'; State = 'PointerOver' }
    $shade = $hover.snapshot.nodes | Where-Object { $_.name -eq 'CoverActivationLayer' } | Select-Object -First 1
    Check ($shade.opacity -eq 1) 'Native button hover did not reveal the dark cover and chevron.'
    $button = $hover.snapshot.nodes | Where-Object { $_.name -eq 'NowPlayingDetails' } | Select-Object -First 1
    Check ($button.padding.left -eq 8 -and $button.padding.top -eq 6) 'Player activation area has no inner padding.'
    $beforeCover = $normal.snapshot.nodes | Where-Object { $_.name -eq 'CoverFrame' } | Select-Object -First 1
    $afterCover = $hover.snapshot.nodes | Where-Object { $_.name -eq 'CoverFrame' } | Select-Object -First 1
    Check (($beforeCover.bounds | ConvertTo-Json -Compress) -eq ($afterCover.bounds | ConvertTo-Json -Compress)) 'Hover changed the cover layout.'
    $normalAgain = Snapshot @{ Target = 'NowPlayingDetails'; State = 'Normal' }
    Check (($normalAgain.snapshot.nodes | Where-Object { $_.name -eq 'CoverActivationLayer' }).opacity -eq 0) 'Cover hover shade remained after leaving the button.'
    $null = Snapshot @{ Tab = 'nowplaying' }
    $loaded = WaitForBackground
    Check ($loaded.playerBackground.effects -and $loaded.playerBackground.ready -and -not $loaded.playerBackground.error) 'Gaussian cover background failed to load.'
    Check ($loaded.playerBackground.blurRadius -eq 40 -and $loaded.playerBackground.width -le 640) 'Background texture or blur is not bounded.'
    $loads = $loaded.playerBackground.loads
    $source = $loaded.playback.sourceId
    $returned = Snapshot @{ Back = $true }
    Check ($returned.lifetime.activeTab -eq 'favorites' -and $returned.playback.sourceId -eq $source) 'Back did not return to the origin without replacing audio.'
    $again = Snapshot @{ Tab = 'nowplaying' }
    Check ($again.playerBackground.ready -and $again.playerBackground.loads -eq $loads) 'Reopening recreated the resident cover background.'
    $null = Snapshot @{ PlaybackAction = 'next' }
    $next = WaitForBackground
    Check ($next.playerBackground.loads -eq $loads + 1 -and $next.playerBackground.ready) 'Changing songs did not replace the background.'
    $shuffled = Snapshot @{ PlaybackAction = 'shuffle' }
    Check ($shuffled.playerBackground.loads -eq $next.playerBackground.loads) 'Shuffling reloaded the current cover.'
    foreach ($theme in @('Dark','Light')) {
        foreach ($width in @(430,800,1245)) {
            $layout = Snapshot @{ Theme = $theme; Width = $width; Height = 780; Capture = $true }
            $player = $layout.snapshot.nodes | Where-Object { $_.automationId -eq 'ExpandedPlayer' } | Select-Object -First 1
            $background = $layout.snapshot.nodes | Where-Object { $_.automationId -eq 'PlayerBackdrop' } | Select-Object -First 1
            $content = $layout.snapshot.nodes | Where-Object { $_.automationId -eq 'ShellContent' } | Select-Object -First 1
            foreach ($node in @($player,$background)) {
                Check ([Math]::Abs($node.bounds.x - $content.bounds.x) -lt 1 -and [Math]::Abs($node.bounds.y - $content.bounds.y) -lt 1 -and
                    [Math]::Abs($node.bounds.width - $content.bounds.width) -lt 1 -and
                    [Math]::Abs($node.bounds.height - $content.bounds.height) -lt 1) 'Player or background does not cover the content and transport area.'
            }
            Check (@($layout.snapshot.nodes | Where-Object { $_.automationId -in @('NowPlayingBar','PageTitle') }).Count -eq 0) 'The expanded player exposes the transport or a heading.'
            Check (@($layout.snapshot.nodes | Where-Object { $_.automationId -eq 'NavPlaylist' }).Count -eq 1) 'The expanded player hides the sidebar.'
            Check ($layout.lifetime.backEnabled -and $layout.playerBackground.ready) 'Expanded player lost its back button or cover.'
            Check ($layout.playerBackground.loads -eq $next.playerBackground.loads) 'Resize or theme changes reloaded the image.'
            foreach ($id in @('PlayerPagePreviousButton','PlayerPagePlayPauseButton','PlayerPageNextButton','TogglePlaylistButton')) {
                $button = $layout.snapshot.nodes | Where-Object { $_.automationId -eq $id } | Select-Object -First 1
                Check ($button.bounds.x -ge 0 -and $button.bounds.x + $button.bounds.width -le $layout.snapshot.width) 'Player controls overflow the client area.'
            }
        }
    }
    $empty = Snapshot @{ PlaybackFixture = 'restore' }
    Check (-not $empty.playerBackground.ready -and $null -eq $empty.playerBackground.width) 'Clearing playback kept the old background.'
    $back = Snapshot @{ Back = $true }
    Check ($back.lifetime.activeTab -eq 'favorites' -and @($back.snapshot.nodes | Where-Object { $_.automationId -eq 'NowPlayingBar' }).Count -eq 1) 'Collapsing did not restore the shell.'
} finally {
    if ($started) {
        $restore = @{ PlaybackFixture = 'restore'; Theme = $initial.savedTheme }
        if ($restore.Theme -notin @('Dark','Light')) { $restore.Theme = 'Default' }
        if ($initial.lifetime.activeTab) { $restore.Tab = $initial.lifetime.activeTab }
        $restore.Width = $initial.windowSize.width / $initial.snapshot.scale
        $restore.Height = $initial.windowSize.height / $initial.snapshot.scale
        $null = Snapshot $restore
    }
}
Write-Output "$checks native expanded-player checks passed."
