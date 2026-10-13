[CmdletBinding()]
param(
    [string]$Svg = 'Assets\appicon-black.svg',

    [string]$Ico = 'Assets\appicon-black.ico',

    [int]$Supersample = 8,

    [string]$Smoothing = 'AntiAlias',

    [string]$Interpolation = 'HighQualityBicubic',

    [string]$PixelOffset = 'Half',

    [switch]$NoExport,

    [switch]$Check,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$canvasSize = 64.0
$backgroundRadius = 14.5
$backgroundFill = '#0A0A0A'
$outlineLeft = 5.5
$outlineTop = 5.5
$outlineRight = 58.5
$outlineBottom = 58.5
$outlineRadius = 12.0
$outlineStroke = '#FFFFFF'
$outlineWidth = 6.0
$outlineGapTop = 25.80
$outlineGapBottom = 38.20
$triangleFill = '#FFFFFF'
$triangle = @(22.47, 17.00, 46.33, 32.00, 22.47, 47.00)

$dibSizes = @(16, 20, 24, 32, 40, 48, 64)
$pngSizes = @(72, 96, 128, 256)
$exportSizes = @(48, 128, 256, 512)

$smoothingMode = [Enum]::Parse([System.Drawing.Drawing2D.SmoothingMode], $Smoothing)
$interpolationMode = [Enum]::Parse([System.Drawing.Drawing2D.InterpolationMode], $Interpolation)
$pixelOffsetMode = [Enum]::Parse([System.Drawing.Drawing2D.PixelOffsetMode], $PixelOffset)

function ConvertFrom-HexColor {
    param([string]$Hex)

    $value = $Hex.TrimStart('#')
    return [System.Drawing.Color]::FromArgb(
        255,
        [Convert]::ToInt32($value.Substring(0, 2), 16),
        [Convert]::ToInt32($value.Substring(2, 2), 16),
        [Convert]::ToInt32($value.Substring(4, 2), 16))
}

function Assert-MasterSvg {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { throw "Master SVG not found: $Path" }

    $text = (Get-Content -LiteralPath $Path -Raw) -replace '\s+', ' '

    $rx = [regex]::Match($text, '<rect[^>]*\brx="([\d.]+)"').Groups[1].Value
    $stroke = [regex]::Match($text, 'stroke-width="([\d.]+)"').Groups[1].Value
    $arcs = ([regex]::Matches($text, 'A12 12 0 0 1')).Count

    if ($rx -ne "$backgroundRadius" -or $stroke -ne "$outlineWidth" -or $arcs -ne 4) {
        throw "The master SVG no longer matches the geometry hard-coded in this script (rx=$rx, stroke-width=$stroke, corner arcs=$arcs). Update the constants at the top of this script before regenerating the icon."
    }
}

function New-RoundedRectPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)

    $d = $R * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-OutlinePath {
    param([single]$K)

    $l = [single]($outlineLeft * $K)
    $t = [single]($outlineTop * $K)
    $r = [single]($outlineRight * $K)
    $b = [single]($outlineBottom * $K)
    $cr = [single]($outlineRadius * $K)
    $d = $cr * 2

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddLine($l + $cr, $t, $r - $cr, $t)
    $path.AddArc($r - $d, $t, $d, $d, 270, 90)
    $path.AddLine($r, $t + $cr, $r, [single]($outlineGapTop * $K))
    $path.StartFigure()
    $path.AddLine($r, [single]($outlineGapBottom * $K), $r, $b - $cr)
    $path.AddArc($r - $d, $b - $d, $d, $d, 0, 90)
    $path.AddLine($r - $cr, $b, $l + $cr, $b)
    $path.AddArc($l, $b - $d, $d, $d, 90, 90)
    $path.AddLine($l, $b - $cr, $l, $t + $cr)
    $path.AddArc($l, $t, $d, $d, 180, 90)
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $pixels = $Size * $Supersample
    $k = [single]($pixels / $canvasSize)

    $bitmap = [System.Drawing.Bitmap]::new($pixels, $pixels, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = $smoothingMode
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $background = New-RoundedRectPath 0 0 ([single]$pixels) ([single]$pixels) ([single]($backgroundRadius * $k))
        $backgroundBrush = [System.Drawing.SolidBrush]::new((ConvertFrom-HexColor $backgroundFill))
        try {
            $graphics.FillPath($backgroundBrush, $background)
        }
        finally {
            $backgroundBrush.Dispose()
            $background.Dispose()
        }

        $outline = New-OutlinePath $k
        $pen = [System.Drawing.Pen]::new((ConvertFrom-HexColor $outlineStroke), [single]($outlineWidth * $k))
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $graphics.DrawPath($pen, $outline)
        }
        finally {
            $pen.Dispose()
            $outline.Dispose()
        }

        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([single]($triangle[0] * $k), [single]($triangle[1] * $k)),
            [System.Drawing.PointF]::new([single]($triangle[2] * $k), [single]($triangle[3] * $k)),
            [System.Drawing.PointF]::new([single]($triangle[4] * $k), [single]($triangle[5] * $k))
        )
        $triangleBrush = [System.Drawing.SolidBrush]::new((ConvertFrom-HexColor $triangleFill))
        try {
            $graphics.FillPolygon($triangleBrush, $points)
        }
        finally {
            $triangleBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    if ($Supersample -le 1) { return $bitmap }

    $target = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $scaler = [System.Drawing.Graphics]::FromImage($target)
    try {
        $scaler.InterpolationMode = $interpolationMode
        $scaler.PixelOffsetMode = $pixelOffsetMode
        $scaler.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $scaler.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $scaler.DrawImage($bitmap, 0, 0, $Size, $Size)
    }
    finally {
        $scaler.Dispose()
    }

    $bitmap.Dispose()
    return $target
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        [byte[]]$bytes = $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }

    return , $bytes
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $s = $Bitmap.Width
    $data = $Bitmap.LockBits([System.Drawing.Rectangle]::new(0, 0, $s, $s), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $raw = [byte[]]::new($stride * $s)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $xorSize = $s * $s * 4
    $maskStride = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $out = [byte[]]::new(40 + $xorSize + $maskStride * $s)

    [BitConverter]::GetBytes([int]40).CopyTo($out, 0)
    [BitConverter]::GetBytes([int]$s).CopyTo($out, 4)
    [BitConverter]::GetBytes([int]($s * 2)).CopyTo($out, 8)
    [BitConverter]::GetBytes([uint16]1).CopyTo($out, 12)
    [BitConverter]::GetBytes([uint16]32).CopyTo($out, 14)
    [BitConverter]::GetBytes([uint32]0).CopyTo($out, 16)
    [BitConverter]::GetBytes([int]$xorSize).CopyTo($out, 20)

    for ($y = 0; $y -lt $s; $y++) {
        [Array]::Copy($raw, ($s - 1 - $y) * $stride, $out, 40 + $y * ($s * 4), $s * 4)
    }

    return , $out
}

function New-IcoBytes {
    param([System.Collections.ArrayList]$Entries)

    $header = [byte[]]::new(6)
    [BitConverter]::GetBytes([uint16]0).CopyTo($header, 0)
    [BitConverter]::GetBytes([uint16]1).CopyTo($header, 2)
    [BitConverter]::GetBytes([uint16]$Entries.Count).CopyTo($header, 4)

    $offset = 6 + $Entries.Count * 16
    $out = [System.Collections.Generic.List[byte]]::new()
    $out.AddRange($header)

    foreach ($entry in $Entries) {
        $dim = [byte]0
        if ($entry.Width -lt 256) { $dim = [byte]$entry.Width }

        $directoryEntry = [byte[]]::new(16)
        $directoryEntry[0] = $dim
        $directoryEntry[1] = $dim
        [BitConverter]::GetBytes([uint16]1).CopyTo($directoryEntry, 4)
        [BitConverter]::GetBytes([uint16]32).CopyTo($directoryEntry, 6)
        [BitConverter]::GetBytes([uint32]$entry.Bytes.Length).CopyTo($directoryEntry, 8)
        [BitConverter]::GetBytes([uint32]$offset).CopyTo($directoryEntry, 12)
        $out.AddRange($directoryEntry)

        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $Entries) {
        $out.AddRange($entry.Bytes)
    }

    return , $out.ToArray()
}

function Get-IcoEntries {
    param([byte[]]$Bytes)

    $entries = [System.Collections.ArrayList]::new()
    if ($Bytes.Length -lt 6) { return $entries }

    $count = [BitConverter]::ToUInt16($Bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + $i * 16
        $width = [int]$Bytes[$o]
        if ($width -eq 0) { $width = 256 }
        [void]$entries.Add([pscustomobject]@{
            Width  = $width
            Offset = [int][BitConverter]::ToUInt32($Bytes, $o + 12)
            Size   = [int][BitConverter]::ToUInt32($Bytes, $o + 8)
        })
    }

    return $entries
}

function Assert-IcoBytes {
    param([byte[]]$Bytes, [int]$ExpectedCount)

    if ($Bytes.Length -lt 6) { throw "The generated icon is too small to contain an icon directory." }
    if ([BitConverter]::ToUInt16($Bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($Bytes, 2) -ne 1) {
        throw "The generated icon does not start with a valid icon directory."
    }

    $count = [BitConverter]::ToUInt16($Bytes, 4)
    if ($count -ne $ExpectedCount) { throw "The generated icon declares $count entries, expected $ExpectedCount." }

    foreach ($entry in (Get-IcoEntries $Bytes)) {
        if ($entry.Offset + $entry.Size -gt $Bytes.Length) {
            throw "The $($entry.Width)px entry points past the end of the file."
        }
    }
}

function Test-BytesEqual {
    param([byte[]]$A, [byte[]]$B)

    if ($null -eq $A -or $null -eq $B) { return $false }
    if ($A.Length -ne $B.Length) { return $false }

    for ($i = 0; $i -lt $A.Length; $i++) {
        if ($A[$i] -ne $B[$i]) { return $false }
    }

    return $true
}

function Get-ExportPath {
    param([string]$IcoPath, [int]$Size)

    return [IO.Path]::ChangeExtension($IcoPath, $null).TrimEnd('.') + "@$Size.png"
}

function Show-IcoComparison {
    param([byte[]]$Reference, [byte[]]$Candidate)

    foreach ($entry in (Get-IcoEntries $Reference)) {
        $match = (Get-IcoEntries $Candidate) | Where-Object { $_.Width -eq $entry.Width } | Select-Object -First 1

        if (-not $match) {
            [pscustomobject]@{ Size = $entry.Width; Reference = $entry.Size; Generated = 0; Differing = $entry.Size }
            continue
        }

        $diff = 0
        if ($match.Size -eq $entry.Size) {
            for ($i = 0; $i -lt $entry.Size; $i++) {
                if ($Reference[$entry.Offset + $i] -ne $Candidate[$match.Offset + $i]) { $diff++ }
            }
        }
        else {
            $diff = [Math]::Max($entry.Size, $match.Size)
        }

        [pscustomobject]@{ Size = $entry.Width; Reference = $entry.Size; Generated = $match.Size; Differing = $diff }
    }
}

Assert-MasterSvg $Svg

$entries = [System.Collections.ArrayList]::new()

foreach ($size in $dibSizes) {
    $bitmap = New-IconBitmap -Size $size
    try { $bytes = Get-DibBytes $bitmap } finally { $bitmap.Dispose() }
    [void]$entries.Add([pscustomobject]@{ Width = $size; Bytes = $bytes; Kind = 'DIB' })
}

foreach ($size in $pngSizes) {
    $bitmap = New-IconBitmap -Size $size
    try { $bytes = Get-PngBytes $bitmap } finally { $bitmap.Dispose() }
    [void]$entries.Add([pscustomobject]@{ Width = $size; Bytes = $bytes; Kind = 'PNG' })
}

$icoBytes = New-IcoBytes $entries
Assert-IcoBytes $icoBytes $entries.Count

$exports = @{}
if (-not $NoExport -or $Check) {
    foreach ($size in $exportSizes) {
        $bitmap = New-IconBitmap -Size $size
        try { $exports[$size] = Get-PngBytes $bitmap } finally { $bitmap.Dispose() }
    }
}

$existingIco = $null
if (Test-Path -LiteralPath $Ico) { $existingIco = [IO.File]::ReadAllBytes($Ico) }

$existingExports = @{}
foreach ($size in $exportSizes) {
    $path = Get-ExportPath $Ico $size
    if (Test-Path -LiteralPath $path) { $existingExports[$size] = [IO.File]::ReadAllBytes($path) }
}

$icoIdentical = Test-BytesEqual $existingIco $icoBytes

$exportIdentical = $true
foreach ($size in $exportSizes) {
    if (-not (Test-BytesEqual $existingExports[$size] $exports[$size])) { $exportIdentical = $false }
}

$renderLabel = "supersample $Supersample, smoothing $Smoothing, interpolation $Interpolation, pixel offset $PixelOffset"

if ($Check) {
    Write-Host "check only, nothing is written"
    Write-Host "icon      : $Ico"
    Write-Host "identical : $icoIdentical  (reference $(if ($existingIco) { $existingIco.Length } else { 0 }) bytes, generated $($icoBytes.Length) bytes)"

    if ($existingIco -and -not $icoIdentical) {
        Show-IcoComparison -Reference $existingIco -Candidate $icoBytes | Format-Table -AutoSize
    }

    foreach ($size in $exportSizes) {
        if (-not $existingExports.ContainsKey($size)) {
            Write-Host "export @$size : no reference file"
            continue
        }
        Write-Host "export @$size : identical=$(Test-BytesEqual $existingExports[$size] $exports[$size])  (reference $($existingExports[$size].Length) bytes, generated $($exports[$size].Length) bytes)"
    }

    Write-Host "render    : $renderLabel"
    return
}

if (-not $Force -and ($null -ne $existingIco -or $existingExports.Count -gt 0)) {
    Write-Host "icon      : $Ico"
    Write-Host "identical : $icoIdentical  (reference $(if ($existingIco) { $existingIco.Length } else { 0 }) bytes, generated $($icoBytes.Length) bytes)"

    if ($existingIco -and -not $icoIdentical) {
        Show-IcoComparison -Reference $existingIco -Candidate $icoBytes | Format-Table -AutoSize
    }

    if (-not $exportIdentical) {
        Write-Host "the @N.png exports would also change"
    }

    Write-Host "refusing to overwrite: this script redraws the icon rather than reusing the previous bitmaps, so the result is never byte-identical to a file it did not produce."
    Write-Host "run again with -Force to write, or with -Check to only look"
    return
}

$tmp = "$Ico.tmp"
$directory = Split-Path -Parent $Ico
if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }

[IO.File]::WriteAllBytes($tmp, $icoBytes)
Assert-IcoBytes ([IO.File]::ReadAllBytes($tmp)) $entries.Count
Move-Item -LiteralPath $tmp -Destination $Ico -Force

$sizeList = ($entries | ForEach-Object { "$($_.Width)$(if ($_.Kind -eq 'PNG') { 'p' } else { 'd' })" }) -join ' '
Write-Host "icon      : $Ico  $((Get-Item -LiteralPath $Ico).Length) bytes"
Write-Host "entries   : $($entries.Count)  ($sizeList)"

if (-not $NoExport) {
    foreach ($size in $exportSizes) {
        $path = Get-ExportPath $Ico $size
        [IO.File]::WriteAllBytes($path, $exports[$size])
        Write-Host "export    : $path  $($exports[$size].Length) bytes"
    }
}

Write-Host "render    : $renderLabel"
