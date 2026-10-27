param(
    [int]$AppProcessId,
    [ValidateSet('sample', 'online', 'restore')][string]$PlaybackFixture,
    [ValidateSet('play', 'pause', 'next', 'previous', 'mute')][string]$PlaybackAction,
    [string]$PlaybackBvid,
    [double]$PlaybackProgress,
    [double]$PlaybackPreview,
    [ValidateSet('empty', 'sample', 'restore')][string]$LibraryFixture,
    [ValidateSet('search', 'favorites', 'settings', 'nowplaying')][string]$Tab,
    [ValidateSet('Light', 'Dark', 'Default')][string]$Theme,
    [double]$Width,
    [double]$Height,
    [AllowEmptyString()][string]$Text,
    [ValidateSet('search', 'navigation')][string]$Focus,
    [ValidateSet('Normal', 'PointerOver', 'Pressed', 'Focused', 'Disabled')][string]$State,
    [string]$Target,
    [switch]$CompareNative,
    [switch]$CheckProtection,
    [switch]$Svg,
    [switch]$Activate,
    [switch]$Capture,
    [int]$SelectionStart,
    [switch]$VisibleReference,
    [switch]$FocusReference,
    [switch]$HoldReference,
    [ValidateSet('Microsoft YaHei','Microsoft YaHei UI','XamlAutoFontFamily')][string]$InputFont,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../artifacts/ui/layout')
)
$ErrorActionPreference = 'Stop'
if (-not $AppProcessId) {
    $instances = @(Get-Process -Name 'bilibili-music-player-windows' -ErrorAction Stop)
    if ($instances.Count -eq 0) { throw 'Start the registered Debug application before inspecting its UI.' }
    if ($instances.Count -ne 1) { throw 'Specify -AppProcessId when multiple application instances are running.' }
    $AppProcessId = $instances[0].Id
}
$request = @{}
foreach ($key in @('PlaybackFixture', 'PlaybackAction', 'PlaybackBvid', 'PlaybackProgress', 'PlaybackPreview', 'LibraryFixture', 'Tab', 'Theme', 'Width', 'Height', 'Text', 'Focus', 'State', 'Target', 'SelectionStart', 'InputFont')) {
    if ($PSBoundParameters.ContainsKey($key)) { $request[$key] = $PSBoundParameters[$key] }
}
if ($CompareNative) { $request.CompareNative = $true }
if ($CheckProtection) { $request.CheckProtection = $true }
if ($Activate) { $request.Activate = $true }
if ($Capture) { $request.Capture = $true }
if ($VisibleReference) { $request.VisibleReference = $true }
if ($FocusReference) { $request.FocusReference = $true }
if ($HoldReference) { $request.HoldReference = $true }
$pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', "BiliMusic.Ui.$AppProcessId", [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 4096, $true)
    $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8, $false, 4096, $true)
    $writer.AutoFlush = $true
    $writer.WriteLine(($request | ConvertTo-Json -Compress))
    $response = $reader.ReadLine() | ConvertFrom-Json -AsHashtable
    if ($response.error) { throw $response.error }
} finally {
    if ($writer) { $writer.Dispose() }
    if ($reader) { $reader.Dispose() }
    $pipe.Dispose()
}
$absolute = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($absolute)) | Out-Null
if ($response.png) {
    [System.IO.File]::WriteAllBytes("$absolute.png", [Convert]::FromBase64String($response.png))
    $response.Remove('png')
    Write-Output "PNG: $absolute.png"
}
$response | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath "$absolute.json" -Encoding utf8
if (-not $Svg) {
    Write-Output "JSON: $absolute.json"
    return
}
function Escape-Xml([string]$value) { [System.Security.SecurityElement]::Escape($value) }
function Number([double]$value) { $value.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture) }
$snapshot = $response.snapshot
$svg = [System.Text.StringBuilder]::new()
[void]$svg.AppendLine("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$(Number $snapshot.width)`" height=`"$(Number $snapshot.height)`" viewBox=`"0 0 $(Number $snapshot.width) $(Number $snapshot.height)`">")
[void]$svg.AppendLine('<title>Measured XAML layout in DIPs; wireframe, not a full WinUI render</title><rect width="100%" height="100%" fill="#f8fafc"/>')
foreach ($node in $snapshot.nodes) {
    $bounds = $node.bounds
    if ($bounds.width -le 0 -or $bounds.height -le 0 -or $node.opacity -eq 0) { continue }
    $label = Escape-Xml "$($node.type) $($node.name) $($node.automationId)"
    [void]$svg.AppendLine("<g><title>$label; x=$($bounds.x), y=$($bounds.y), width=$($bounds.width), height=$($bounds.height)</title>")
    if ($node.name -or $node.automationId -or $node.type -eq 'TextBlock') {
        [void]$svg.AppendLine("<rect x=`"$(Number $bounds.x)`" y=`"$(Number $bounds.y)`" width=`"$(Number $bounds.width)`" height=`"$(Number $bounds.height)`" fill=`"none`" stroke=`"#64748b`" stroke-opacity=`"0.45`" stroke-width=`"0.5`"/>")
    }
    if ($node.type -eq 'TextBlock' -and $node.text) {
        $textValue = Escape-Xml $node.text
        $family = Escape-Xml $node.fontFamily
        [void]$svg.AppendLine("<text x=`"$(Number ($bounds.x + $node.padding.left))`" y=`"$(Number $node.baselineY)`" font-family=`"$family`" font-size=`"$(Number $node.fontSize)`" fill=`"#0f172a`">$textValue</text>")
        [void]$svg.AppendLine("<path d=`"M $(Number $bounds.x) $(Number $node.baselineY) h $(Number $bounds.width)`" stroke=`"#e11d48`" stroke-width=`"0.5`"/>")
    }
    [void]$svg.AppendLine('</g>')
}
[void]$svg.AppendLine('</svg>')
$svg.ToString() | Set-Content -LiteralPath "$absolute.svg" -Encoding utf8
Write-Output "JSON: $absolute.json"
Write-Output "SVG:  $absolute.svg"
