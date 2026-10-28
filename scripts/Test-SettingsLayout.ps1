param([int]$AppProcessId)
$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-Ui.ps1'
$output = Join-Path $PSScriptRoot '../artifacts/ui/settings-regression'
$checks = 0
$sample = 0
function Snapshot([hashtable]$request = @{}) {
    if ($AppProcessId) { $request.AppProcessId = $AppProcessId }
    $script:sample++
    $path = "$output-$script:sample"
    & $inspect @request -OutputPath $path | Out-Null
    Get-Content -LiteralPath "$path.json" -Raw | ConvertFrom-Json -AsHashtable
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
$initial = Snapshot
try {
    foreach ($theme in @('Light','Dark')) {
        foreach ($width in @(430,760,1245)) {
            $s = Snapshot @{ Tab = 'settings'; Theme = $theme; Width = $width; Height = 900; Capture = $true }
            $nodes = $s.snapshot.nodes
            $title = $nodes | Where-Object { $_.automationId -eq 'PageTitle' } | Select-Object -First 1
            Check ($title.text -eq '设置' -and $title.fontFamily -eq 'Microsoft YaHei') 'Settings title or typeface changed.'
            Check ($title.fontSize -eq 28) 'Settings title differs from the shared page title size.'
            Check (@($nodes | Where-Object { $_.text -match '192K|音频质量' }).Count -eq 0) 'The obsolete quality selector remains.'
            Check (@($nodes | Where-Object { $_.automationId -eq 'LibraryAccountButton' }).Count -eq 0) 'Legacy account controls remain.'
            foreach ($id in @('SettingsAccountButton','SettingsSyncButton','QuickPlayGesture','QuickPlayScope','QuickPlayDestination',
                              'AutoPlayToggle','FadeToggle','ThemeIndex','AnimationsToggle','CloseBehavior','PauseInBackgroundToggle','AudioCacheToggle','ClearCacheButton')) {
                $control = @($nodes | Where-Object { $_.automationId -eq $id })
                Check ($control.Count -eq 1) "Missing or duplicate settings control: $id"
                $bounds = $control[0].bounds
                Check ($bounds.x -ge 48 -and $bounds.x + $bounds.width -le $s.snapshot.width - 24) "Settings control overflows horizontally: $id"
            }
            foreach ($control in @($nodes | Where-Object { $_.name -eq 'RowControl' })) {
                Check ($control.row -eq $(if ($s.snapshot.width -ge 760) { 0 } else { 1 })) 'Setting controls did not adapt to window width.'
            }
            $pink = @($nodes | Where-Object { $_.name -eq 'SwitchKnobBounds' -and $_.background.color -eq '#FFFB7185' })
            Check ($pink.Count -ge 1) 'Enabled switches are not using the app pink.'
        }
    }
} finally {
    $restore = @{ Tab = $initial.lifetime.activeTab; Theme = $initial.requestedTheme
        Width = $initial.windowSize.width / $initial.snapshot.scale
        Height = $initial.windowSize.height / $initial.snapshot.scale }
    $null = Snapshot $restore
}
Write-Output "$checks native settings-layout checks passed."
