param([int]$AppProcessId)
$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$output = Join-Path $PSScriptRoot '../artifacts/ui/queue-regression'
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
function QueueIds($state) { ($state.playback.queue | ForEach-Object { $_.bvid }) -join ',' }
function CheckQueue($state) {
    $entries = @($state.playback.queue)
    Check ($entries.Count -eq 4 -and $state.playback.index -eq 0) 'Current track is not the queue head.'
    Check ($entries[0].current -and $entries[0].sequence -eq '') 'Current track has a numeric sequence.'
    Check ((($entries | Select-Object -Skip 1 | ForEach-Object { $_.sequence }) -join ',') -eq '1,2,3') 'Upcoming sequence numbers are incorrect.'
}
$initial = Snapshot
Check ($initial.playback.count -eq 0) 'Run with an empty queue; existing playback is preserved.'
$started = $false
try {
    $started = $true
    $null = Snapshot @{ PlaybackFixture = 'queue'; Tab = 'playlist'; Width = 1245; Height = 780; Theme = 'Dark' }
    $paused = Snapshot @{ PlaybackAction = 'pause'; PlaybackProgress = .4 }
    CheckQueue $paused
    $source = $paused.playback.sourceId
    $position = $paused.playback.position
    $shuffled = Snapshot @{ PlaybackAction = 'shuffle' }
    CheckQueue $shuffled
    Check ($shuffled.playback.queue[0].bvid -eq $paused.playback.queue[0].bvid) 'Shuffle changed the current entry.'
    Check ((($shuffled.playback.queue.bvid | Sort-Object) -join ',') -eq (($paused.playback.queue.bvid | Sort-Object) -join ',')) 'Shuffle lost or duplicated entries.'
    Check ($source -gt 0 -and $shuffled.playback.sourceId -eq $source -and [Math]::Abs($shuffled.playback.position - $position) -lt .1) 'Shuffle reloaded or moved the current audio.'
    Check (-not $shuffled.playback.playing -and $shuffled.playback.mode -eq 'Loop') 'Shuffle changed the mode or resumed playback.'
    $next = Snapshot @{ PlaybackAction = 'next' }
    CheckQueue $next
    Check ($next.playback.queue[0].bvid -eq $shuffled.playback.queue[1].bvid) 'Next did not rotate the head to the tail.'
    $previous = Snapshot @{ PlaybackAction = 'previous' }
    Check ((QueueIds $previous) -eq (QueueIds $shuffled)) 'Previous did not reverse next.'
    $single = Snapshot @{ PlaybackAction = 'mode' }
    Check ($single.playback.mode -eq 'Single' -and -not $single.playback.queue[0].dimmed) 'Single repeat dimmed the current entry.'
    Check (@($single.playback.queue | Select-Object -Skip 1 | Where-Object { -not $_.dimmed }).Count -eq 0) 'Upcoming entries did not dim in single repeat.'
    $rows = @($single.snapshot.nodes | Where-Object { $_.name -eq 'QueueRow' })
    Check (@($rows | Where-Object { [Math]::Abs($_.opacity - .4) -lt .001 }).Count -eq 3) 'Actual XAML rows did not dim.'
    $nextSingle = Snapshot @{ PlaybackAction = 'next' }
    Check ($nextSingle.playback.mode -eq 'Single' -and $nextSingle.playback.queue[0].bvid -eq $single.playback.queue[1].bvid) 'Manual next failed in single repeat.'
    $loop = Snapshot @{ PlaybackAction = 'mode' }
    Check ($loop.playback.mode -eq 'Loop' -and @($loop.playback.queue | Where-Object { $_.dimmed }).Count -eq 0) 'List loop did not restore row opacity.'
    foreach ($theme in @('Dark', 'Light')) {
        foreach ($width in @(430, 1245)) {
            $layout = Snapshot @{ Tab = 'playlist'; Theme = $theme; Width = $width; Height = 780; Capture = $true }
            $buttons = @($layout.snapshot.nodes | Where-Object { $_.automationId -in @('QueuePlayMode', 'ShuffleQueueButton', 'ClearQueueButton') })
            Check ($buttons.Count -eq 3) 'Queue toolbar is missing controls.'
            foreach ($button in $buttons) {
                Check ($button.bounds.x -ge 0 -and $button.bounds.x + $button.bounds.width -le $layout.snapshot.width + 1) 'Queue toolbar overflows the window.'
            }
            $now = $layout.snapshot.nodes | Where-Object { $_.automationId -eq 'NavNowPlaying' } | Select-Object -First 1
            $settings = $layout.snapshot.nodes | Where-Object { $_.automationId -eq 'NavSettings' } | Select-Object -First 1
            Check ($now.bounds.y -gt $layout.snapshot.height / 2 -and $now.bounds.y -lt $settings.bounds.y) 'Now playing is not above settings in the footer.'
            $thumbnails = @($layout.snapshot.nodes | Where-Object { $_.type -eq 'AspectRatioPanel' -and $_.bounds.width -gt 0 -and $_.bounds.height -gt 0 })
            Check ($thumbnails.Count -ge 4) 'Queue thumbnail frames are missing.'
            foreach ($thumbnail in $thumbnails) {
                Check ([Math]::Abs($thumbnail.bounds.height - $thumbnail.bounds.width * 9 / 16) -lt 1) 'A video thumbnail is not 16:9.'
            }
            $artistTexts = @($layout.snapshot.nodes | Where-Object { $_.name -eq 'ArtistText' })
            $compactArtists = @($layout.snapshot.nodes | Where-Object { $_.name -eq 'CompactArtistText' })
            if ($width -ge 850) {
                Check ($artistTexts.Count -eq 4 -and $compactArtists.Count -eq 0) 'Wide queue rows did not switch artist columns.'
                $header = $layout.snapshot.nodes | Where-Object { $_.name -eq 'HeaderArtist' } | Select-Object -First 1
                foreach ($artist in $artistTexts) {
                    # ItemsStackPanel rounds its row extent separately from the
                    # outer header at fractional display scales (up to 2 px).
                    Check ([Math]::Abs($artist.bounds.x - $header.bounds.x) -lt 2) 'Artist cells do not align with the table header.'
                }
            } else {
                Check ($artistTexts.Count -eq 0 -and $compactArtists.Count -eq 4) 'Compact queue rows did not merge the artist into the title column.'
            }
        }
    }
    $nowPlaying = Snapshot @{ Tab = 'nowplaying'; Width = 1245; Height = 780 }
    $cover = $nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'AudioPlayerCover' } | Select-Object -First 1
    Check ([Math]::Abs($cover.bounds.height - $cover.bounds.width * 9 / 16) -lt 1) 'Now playing cover is not 16:9.'
    Check ($nowPlaying.lifetime.activeTab -eq 'nowplaying' -and $nowPlaying.lifetime.backEnabled) 'Expanded player cannot return to the previous page.'
    $overlay = $nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'ExpandedPlayer' } | Select-Object -First 1
    $content = $nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'ShellContent' } | Select-Object -First 1
    Check ([Math]::Abs($overlay.bounds.x - $content.bounds.x) -lt 1 -and [Math]::Abs($overlay.bounds.y - $content.bounds.y) -lt 1 -and
        [Math]::Abs($overlay.bounds.width - $content.bounds.width) -lt 1 -and
        [Math]::Abs($overlay.bounds.height - $content.bounds.height) -lt 1) 'Expanded player does not cover the content and transport area.'
    Check (@($nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'NavPlaylist' }).Count -eq 1) 'Expanded player hides the sidebar.'
    Check (@($nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'NowPlayingBar' }).Count -eq 0) 'Transport is exposed below the expanded player.'
    Check (@($nowPlaying.snapshot.nodes | Where-Object { $_.automationId -eq 'PageTitle' }).Count -eq 0) 'Expanded player still has a page heading.'
} finally {
    if ($started) {
        $restore = @{ PlaybackFixture = 'restore'; Theme = $initial.savedTheme }
        if ($restore.Theme -notin @('Dark', 'Light')) { $restore.Theme = 'Default' }
        if ($initial.lifetime.activeTab) { $restore.Tab = $initial.lifetime.activeTab }
        $restore.Width = $initial.windowSize.width / $initial.snapshot.scale
        $restore.Height = $initial.windowSize.height / $initial.snapshot.scale
        $null = Snapshot $restore
    }
}
Write-Output "$checks native playback queue checks passed."
