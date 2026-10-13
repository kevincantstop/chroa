[CmdletBinding()]
param(
    [int]$Width = 1600,

    [int]$Quality = 90,

    [string]$Source = 'screenshots',

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

if ($Width -lt 1) { throw "Width must be a positive integer." }
if ($Quality -lt 1 -or $Quality -gt 100) { throw "Quality must be between 1 and 100." }

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $repoRoot $Source }

if (-not (Test-Path -LiteralPath $sourceDir)) { throw "Screenshot folder not found: $sourceDir" }

$pngs = @(Get-ChildItem -LiteralPath $sourceDir -File -Filter '*.png' | Sort-Object Name)
if ($pngs.Count -eq 0) { throw "No PNG files in $sourceDir. Save your screenshots there and run this again." }

$jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
if (-not $jpegCodec) { throw "No JPEG encoder is available on this machine." }

$encoderParams = [System.Drawing.Imaging.EncoderParameters]::new(1)
$encoderParams.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)

$cleanLabel = if ($Clean) { 'on, source PNGs are deleted only after every conversion succeeds' } else { 'off, source PNGs are kept' }

Write-Host "ChroaVideoPlayer screenshots -> $sourceDir"
Write-Host "target    : $Width px wide, JPEG quality $Quality"
Write-Host "clean     : $cleanLabel"
Write-Host "sources   : $($pngs.Count) PNG file(s)"

$results = @()

foreach ($png in $pngs) {
    Write-Host ''
    Write-Host "==> $($png.Name)"

    $jpgPath = Join-Path $sourceDir ($png.BaseName + '.jpg')
    $tmpPath = "$jpgPath.tmp"
    $previousLength = if (Test-Path -LiteralPath $jpgPath) { (Get-Item -LiteralPath $jpgPath).Length } else { 0 }

    $source = [System.Drawing.Image]::FromFile($png.FullName)
    try {
        $outWidth = if ($source.Width -lt $Width) { $source.Width } else { $Width }
        $outHeight = [int][Math]::Round($source.Height * $outWidth / $source.Width)

        if ($outWidth -lt $Width) {
            Write-Host "    warning: the source is only $($source.Width) px wide, so $outWidth px was written instead (never upscaled)"
        }

        $bitmap = [System.Drawing.Bitmap]::new($outWidth, $outHeight, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.DrawImage($source, 0, 0, $outWidth, $outHeight)
            }
            finally {
                $graphics.Dispose()
            }

            if (Test-Path -LiteralPath $tmpPath) { Remove-Item -LiteralPath $tmpPath -Force }
            $bitmap.Save($tmpPath, $jpegCodec, $encoderParams)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    $verify = [System.Drawing.Image]::FromFile($tmpPath)
    try {
        $actualWidth = $verify.Width
        $actualHeight = $verify.Height
    }
    finally {
        $verify.Dispose()
    }

    $tmpLength = (Get-Item -LiteralPath $tmpPath).Length
    if ($actualWidth -ne $outWidth -or $actualHeight -ne $outHeight -or $tmpLength -lt 10000) {
        Remove-Item -LiteralPath $tmpPath -Force
        throw "The conversion of $($png.Name) produced a bad file ($actualWidth x $actualHeight, $tmpLength bytes). No file was replaced."
    }

    Move-Item -LiteralPath $tmpPath -Destination $jpgPath -Force

    $jpgLength = (Get-Item -LiteralPath $jpgPath).Length
    Write-Host ("    wrote {0}  {1}x{2}  {3:N0} KB" -f (Split-Path -Leaf $jpgPath), $actualWidth, $actualHeight, ($jpgLength / 1KB))
    if ($previousLength -gt 0) {
        Write-Host ("    replaced the previous {0:N0} KB file" -f ($previousLength / 1KB))
    }

    $previousKB = if ($previousLength -gt 0) { [math]::Round($previousLength / 1KB) } else { $null }

    $results += [pscustomobject]@{
        Source     = $png.Name
        Output     = (Split-Path -Leaf $jpgPath)
        Size       = "$actualWidth x $actualHeight"
        SourceKB   = [math]::Round($png.Length / 1KB)
        OutputKB   = [math]::Round($jpgLength / 1KB)
        PreviousKB = $previousKB
    }
}

if ($Clean) {
    Write-Host ''
    foreach ($png in $pngs) {
        Remove-Item -LiteralPath $png.FullName -Force
        Write-Host "    removed $($png.Name)"
    }
}

$keptNames = @($pngs | ForEach-Object { $_.BaseName })
$orphans = @(Get-ChildItem -LiteralPath $sourceDir -File -Filter '*.jpg' | Where-Object { $keptNames -notcontains $_.BaseName })

Write-Host ''
$results | Format-Table -AutoSize
Write-Host "converted   $($results.Count) file(s) at $Width px wide, JPEG quality $Quality"

if ($Clean) {
    Write-Host "source PNGs deleted"
}
else {
    Write-Host "source PNGs kept; re-run with -Clean to delete them"
}

if ($orphans.Count -gt 0) {
    Write-Host "not updated $($orphans.Count) file(s) with no matching PNG: $(($orphans | ForEach-Object { $_.Name }) -join ', ')"
}

Write-Host "output      $sourceDir"
