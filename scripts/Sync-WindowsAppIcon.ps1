# Regenerate Windows icon formats from the same original used by Android.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot '../mobile/assets/icon/app_icon.png'
$assetRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../windows/Assets'))
$source = [System.Drawing.Bitmap]::FromFile([System.IO.Path]::GetFullPath($sourcePath))

function New-IconBitmap([int]$Width, [int]$Height, [int]$IconSize) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $destination = [System.Drawing.Rectangle]::new(($Width - $IconSize) / 2, ($Height - $IconSize) / 2, $IconSize, $IconSize)
        $graphics.DrawImage($source, $destination, 0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    } catch {
        $bitmap.Dispose()
        throw
    } finally {
        $graphics.Dispose()
        $attributes.Dispose()
    }
    return $bitmap
}

function Save-IconPng([string]$Name, [int]$Width, [int]$Height, [int]$IconSize) {
    $bitmap = New-IconBitmap $Width $Height $IconSize
    try { $bitmap.Save((Join-Path $assetRoot $Name), [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}

try {
    Save-IconPng 'Square44x44Logo.scale-200.png' 88 88 88
    Save-IconPng 'Square150x150Logo.scale-200.png' 300 300 300
    Save-IconPng 'StoreLogo.png' 50 50 50
    Save-IconPng 'LockScreenLogo.scale-200.png' 48 48 48
    Save-IconPng 'Wide310x150Logo.scale-200.png' 620 300 300
    Save-IconPng 'SplashScreen.scale-200.png' 1240 600 300
    foreach ($size in @(16, 24, 32, 48, 256)) {
        Save-IconPng "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size $size $size
    }

    # ICO contains a separate PNG frame for each shell/tray size. No upscaling
    # or icon redesign: every frame is resized directly from the 2048px source.
    $frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)) {
        $bitmap = New-IconBitmap $size $size $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            @{ Size = $size; Bytes = $stream.ToArray() }
        } finally {
            $bitmap.Dispose()
            $stream.Dispose()
        }
    }
    $file = [System.IO.File]::Create((Join-Path $assetRoot 'AppIcon.ico'))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
    Write-Output 'Windows package, executable and tray icons regenerated from mobile/assets/icon/app_icon.png.'
} finally { $source.Dispose() }
