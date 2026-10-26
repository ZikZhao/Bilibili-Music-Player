$ErrorActionPreference = 'Stop'
$exportRoot = Join-Path $PSScriptRoot '../artifacts/ui/checks'
$inspection = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$checks = 0
function Assert-Layout([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Close-Enough([double]$left, [double]$right) { [Math]::Abs($left - $right) -lt 0.01 }
function Is-Descendant($node, [int]$ancestorId, $nodes) {
    while ($null -ne $node.parent) {
        if ($node.parent -eq $ancestorId) { return $true }
        $node = $nodes[$node.parent]
    }
    return $false
}
$initialPath = Join-Path $exportRoot 'initial'
& $inspection -OutputPath $initialPath | Out-Null
$initial = Get-Content -LiteralPath "$initialPath.json" -Raw | ConvertFrom-Json -AsHashtable
$windowInset = $initial.windowSize.width / $initial.snapshot.scale - $initial.snapshot.width
foreach ($theme in @('Light', 'Dark')) {
    foreach ($sample in @(@{ name = 'empty'; text = '' }, @{ name = 'typed'; text = '搜索 Bilibili 音乐视频' })) {
        foreach ($state in @('Normal', 'PointerOver', 'Focused')) {
            $label = "$theme-$($sample.name)-$state"
            $path = Join-Path $exportRoot $label
            $focus = if ($state -eq 'Focused') { 'search' } else { 'navigation' }
            & $inspection -Tab search -Theme $theme -Text $sample.text -Focus $focus -State $state -CompareNative -OutputPath $path | Out-Null
            $report = Get-Content -LiteralPath "$path.json" -Raw | ConvertFrom-Json -AsHashtable
            $actual = $report.nativeComparison.actual.nodes
            $reference = $report.nativeComparison.reference.nodes
            Assert-Layout ($actual[0].templateStatus -in @('Native prefix attached', 'Reused prefix')) "$label has no native prefix"
            Assert-Layout (@($actual | Where-Object automationId -eq 'SubmitSearchButton').Count -eq 1) "$label has duplicate/missing search buttons"
            foreach ($name in @('ContentElement', 'PlaceholderTextContentPresenter')) {
                $current = @($actual | Where-Object name -eq $name)
                $native = @($reference | Where-Object name -eq $name)
                if ($name -eq 'PlaceholderTextContentPresenter' -and $sample.name -eq 'typed') { continue }
                Assert-Layout ($current.Count -eq 1 -and $native.Count -eq 1) "$label missing $name"
                $current = $current[0]; $native = $native[0]
                Assert-Layout (Close-Enough $current.bounds.y $native.bounds.y) "$label $name native layer moved"
                Assert-Layout (Close-Enough $current.bounds.height $native.bounds.height) "$label $name height differs"
                Assert-Layout ($current.fontFamily -eq $native.fontFamily -and $current.fontSize -eq $native.fontSize) "$label font differs"
                foreach ($edge in @('left','top','right','bottom')) {
                    Assert-Layout (Close-Enough $current.padding[$edge] $native.padding[$edge]) "$label $name native padding differs"
                }
                Assert-Layout (Close-Enough ($current.bounds.x - $native.bounds.x) 32) "$label $name horizontal prefix differs"
                if ($name -eq 'PlaceholderTextContentPresenter') {
                    Assert-Layout (Close-Enough $current.baselineOffset $native.baselineOffset) "$label placeholder baseline differs"
                }
            }
            $textPresenter = $actual | Where-Object name -eq 'ScrollContentPresenter'
            $nativePresenter = $reference | Where-Object name -eq 'ScrollContentPresenter'
            $pixelTolerance = 1 / $report.snapshot.scale + 0.001
            Assert-Layout (Close-Enough $textPresenter.bounds.y $nativePresenter.bounds.y) "$label editor/caret vertical layout changed"
            Assert-Layout ([Math]::Abs(($textPresenter.bounds.x - $nativePresenter.bounds.x) - 32) -le $pixelTolerance) "$label editor icon spacing differs"
            Assert-Layout (Close-Enough $actual[0].bounds.height $actual[0].desiredHeight) "$label input stretched beyond its natural height"
            $chrome = $actual | Where-Object name -eq 'BorderElement'
            $nativeChrome = $reference | Where-Object name -eq 'BorderElement'
            foreach ($property in @('background','borderBrush','borderThickness')) {
                Assert-Layout (($chrome[$property] | ConvertTo-Json -Depth 8 -Compress) -eq ($nativeChrome[$property] | ConvertTo-Json -Depth 8 -Compress)) "$label native $property differs"
            }
            $page = $report.snapshot.nodes | Where-Object name -eq 'SearchPageRoot'
            Assert-Layout ($null -eq $page.background) "$label added a separate page background"
            $history = $report.snapshot.nodes | Where-Object automationId -eq 'SearchHistoryItems'
            foreach ($tag in @($report.snapshot.nodes | Where-Object { $_.type -eq 'Button' -and (Is-Descendant $_ $history.id $report.snapshot.nodes) })) {
                foreach ($edge in @('left','top','right','bottom')) {
                    Assert-Layout ($tag.borderThickness[$edge] -eq 0) "$label history tag has an outline"
                }
                $expectedColor = if ($theme -eq 'Light') { '#FFE5E5E5' } else { '#FF353535' }
                Assert-Layout ($tag.background.color -eq $expectedColor) "$label history background is not neutral gray"
            }
            Write-Output "PASS $label"
        }
    }
    foreach ($state in @('Normal','PointerOver','Pressed')) {
        $path = Join-Path $exportRoot "$theme-button-$state"
        & $inspection -Theme $theme -Focus navigation -State $state -Target SubmitSearchButton -OutputPath $path | Out-Null
        $report = Get-Content -LiteralPath "$path.json" -Raw | ConvertFrom-Json -AsHashtable
        $button = $report.snapshot.nodes | Where-Object automationId -eq 'SubmitSearchButton'
        $templateRoot = @($report.snapshot.nodes | Where-Object parent -eq $button.id)[0]
        Assert-Layout ($templateRoot.background.kind -eq 'solid' -and $templateRoot.background.color.Substring(1,2) -eq '00') "$theme $state button template is not transparent"
        Assert-Layout ($templateRoot.borderThickness.left -eq 0 -and $templateRoot.borderThickness.top -eq 0 -and $templateRoot.borderThickness.right -eq 0 -and $templateRoot.borderThickness.bottom -eq 0) "$theme $state button has an outline"
        Write-Output "PASS $theme button $state"
    }
}
foreach ($width in @(460,743,744,1250)) {
    $path = Join-Path $exportRoot "width-$width"
    & $inspection -Width ($width + $windowInset) -Height 800 -Text '' -Focus navigation -State Normal -Target SubmitSearchButton -OutputPath $path | Out-Null
    $report = Get-Content -LiteralPath "$path.json" -Raw | ConvertFrom-Json -AsHashtable
    $page = $report.snapshot.nodes | Where-Object name -eq 'SearchPageRoot'
    $entry = $report.snapshot.nodes | Where-Object name -eq 'SearchEntryContainer'
    $header = $report.snapshot.nodes | Where-Object name -eq 'SearchHeader'
    $expectedRow = if ($report.snapshot.width -lt 744) { 1 } else { 0 }
    Assert-Layout ($entry.row -eq $expectedRow) "Adaptive header wrong at $($report.snapshot.width) DIPs"
    Assert-Layout ([Math]::Abs($report.snapshot.width - $width) -lt 0.67) "Requested client width $width was not reached"
    Assert-Layout ($entry.bounds.x -ge $header.bounds.x - 0.01 -and $entry.bounds.x + $entry.bounds.width -le $header.bounds.x + $header.bounds.width + 0.01) "Search input overflows at width $width"
    Write-Output "PASS width $width (client $($report.snapshot.width) DIP)"
}
$restoreWidth = $initial.windowSize.width / $initial.snapshot.scale
$restoreHeight = $initial.windowSize.height / $initial.snapshot.scale
& $inspection -Width $restoreWidth -Height $restoreHeight -Theme Default -Text '' -Focus navigation -State Normal -Target SearchTextBox -OutputPath (Join-Path $exportRoot 'restored') | Out-Null
Write-Output "$checks layout/style assertions passed. No desktop input was injected."
