param([int]$AppProcessId, [ValidateSet('Dark','Light')][string]$Theme = 'Dark')
$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$output = Join-Path $PSScriptRoot '../artifacts/ui/tab-presentation'
$checks = 0
$sample = 0
function Snapshot([hashtable]$request = @{}) {
    if ($AppProcessId) { $request.AppProcessId = $AppProcessId }
    $script:sample++
    $path = "$output-$Theme-$script:sample"
    & $inspect @request -OutputPath $path | Out-Null
    Get-Content -LiteralPath "$path.json" -Raw | ConvertFrom-Json -AsHashtable
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
$initial = Snapshot @{ Theme = $Theme }
try {
    foreach ($tab in @('settings','playlist','search')) {
        $s = Snapshot @{ ProbeTabPresentation = $tab }
        $p = $s.tabPresentationProbe
        Check $p.preparing "The $tab page was already prepared. Restart the app before this cold-navigation test."
        Check $p.previousVisible 'The old page disappeared before the new one was ready.'
        Check ($p.nextOpacity -eq 0) 'An unprepared page was exposed.'
        Check ($p.frames -ge 2 -and -not $p.gap) 'The first render pass exposed an empty interval.'
        Check ($p.settled -and $p.themeMatches -and $p.previousCollapsed) 'The new page did not replace the old page cleanly.'
    }
    foreach ($tab in @('playlist','settings','search')) {
        $s = Snapshot @{ ProbeTabPresentation = $tab }
        Check (-not $s.tabPresentationProbe.preparing -and $s.tabPresentationProbe.nextOpacity -eq 1) 'Revisiting a prepared page delayed its presentation.'
        Check ($s.tabPresentationProbe.settled -and $s.tabPresentationProbe.themeMatches) 'A resident page lost its theme or visibility.'
    }
} finally {
    $null = Snapshot @{ Tab = $initial.lifetime.activeTab; Theme = $initial.requestedTheme }
}
Write-Output "$checks native $Theme cold-navigation checks passed."
