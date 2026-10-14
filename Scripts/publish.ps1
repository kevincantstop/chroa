[CmdletBinding()]
param(
    [string[]]$Arch = @('x64', 'x86', 'arm64'),

    [string]$Configuration = 'Release',

    [switch]$FrameworkDependent,

    [switch]$NoZip,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$validArch = @('x64', 'x86', 'arm64')
$Arch = @($Arch | ForEach-Object { "$_".Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Arch.Count -eq 0) {
    throw "No architecture selected. Valid values: $($validArch -join ', ')."
}
$unknownArch = @($Arch | Where-Object { $validArch -notcontains $_ })
if ($unknownArch.Count -gt 0) {
    throw "Unknown architecture: $($unknownArch -join ', '). Valid values: $($validArch -join ', ')."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repoRoot 'ChroaVideoPlayer'
$csproj = Join-Path $appDir 'ChroaVideoPlayer.csproj'
$releaseDir = Join-Path $appDir (Join-Path 'bin' $Configuration)
$nativeLib = Join-Path $appDir 'native\libmpv-2.dll'
$ridMap = @{ x64 = 'win-x64'; x86 = 'win-x86'; arm64 = 'win-arm64' }
$requiredNatives = @('av_libglesv2.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')

$selfContained = -not $FrameworkDependent
$selfContainedArg = if ($selfContained) { 'true' } else { 'false' }
$nameSuffix = if ($selfContained) { '' } else { '-net10' }
$runtimeLabel = if ($selfContained) { 'bundled' } else { 'requires .NET 10' }

if (-not (Test-Path -LiteralPath $csproj)) { throw "Project file not found: $csproj" }

[xml]$project = Get-Content -LiteralPath $csproj -Raw
$version = $project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    $version = $project.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1
}
if (-not $version) {
    throw "No <Version> in $csproj. Add one, for example <Version>1.0.1</Version>."
}
$version = $version.Trim()

if (-not (Test-Path -LiteralPath $nativeLib)) {
    throw "Missing $nativeLib. The native folder is excluded by .gitignore, so a fresh clone does not have it. Place a libmpv build there before packaging, otherwise the player cannot open any file."
}

if ($Clean -and (Test-Path -LiteralPath $releaseDir)) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

Write-Host "ChroaVideoPlayer $version -> $releaseDir"
Write-Host "runtime   : $runtimeLabel"

$results = @()

foreach ($archName in $Arch) {
    $rid = $ridMap[$archName]
    $outDir = Join-Path $releaseDir $archName

    Write-Host ''
    Write-Host "==> $rid"

    if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }

    & dotnet publish $csproj -c $Configuration -r $rid --self-contained $selfContainedArg -o $outDir --nologo -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)" }

    $pdbs = @(Get-ChildItem -LiteralPath $outDir -Recurse -File -Filter '*.pdb')
    if ($pdbs.Count -gt 0) {
        $pdbMB = ($pdbs | Measure-Object Length -Sum).Sum / 1MB
        $pdbs | Remove-Item -Force
        Write-Host ("    dropped {0} .pdb ({1:N1} MB)" -f $pdbs.Count, $pdbMB)
    }

    if (-not (Test-Path -LiteralPath (Join-Path $outDir 'ChroaVideoPlayer.exe'))) {
        throw "ChroaVideoPlayer.exe is missing from the $rid output."
    }

    foreach ($nativeName in $requiredNatives) {
        if (-not (Get-ChildItem -LiteralPath $outDir -Recurse -File -Filter $nativeName | Select-Object -First 1)) {
            throw "Native library $nativeName is missing from the $rid output."
        }
    }

    if (-not (Get-ChildItem -LiteralPath $outDir -Recurse -File -Filter 'libmpv-2.dll' | Select-Object -First 1)) {
        throw "libmpv-2.dll is missing from the $rid output."
    }

    $stray = @(Get-ChildItem -LiteralPath $outDir -Recurse -Directory | Where-Object { $_.Name -like 'linux-*' -or $_.Name -eq 'osx' })
    if ($stray.Count -gt 0) {
        throw "Non-Windows runtime folders left in $outDir : $($stray.Name -join ', ')"
    }

    $runtimeConfigs = @(Get-ChildItem -LiteralPath $outDir -Recurse -File -Filter '*.runtimeconfig.json')
    $runtimeConfig = @($runtimeConfigs | Where-Object Name -eq 'ChroaVideoPlayer.runtimeconfig.json') | Select-Object -First 1
    if (-not $runtimeConfig) { $runtimeConfig = $runtimeConfigs | Select-Object -First 1 }
    if (-not $runtimeConfig) {
        throw "runtimeconfig.json is missing from the $rid output, so the package cannot be checked for a bundled runtime."
    }

    $runtimeConfigText = Get-Content -LiteralPath $runtimeConfig.FullName -Raw
    $declaresFramework = [bool]($runtimeConfigText -match '"framework"\s*:')
    $declaresIncluded = [bool]($runtimeConfigText -match '"includedFrameworks"\s*:')
    $coreclr = Get-ChildItem -LiteralPath $outDir -Recurse -File -Filter 'coreclr.dll' | Select-Object -First 1
    $bundledVersion = '-'
    if ($declaresIncluded) {
        $m = [regex]::Match($runtimeConfigText, '"includedFrameworks"\s*:\s*\[.*?"version"\s*:\s*"([^"]+)"', 'Singleline')
        if ($m.Success) { $bundledVersion = $m.Groups[1].Value }
    }

    if ($selfContained) {
        if (-not $coreclr) {
            throw "coreclr.dll is missing from the $rid output: the package is not self-contained and would still demand an installed .NET runtime. Refusing to package it."
        }
        if ($declaresFramework -and -not $declaresIncluded) {
            throw "The $rid runtimeconfig.json declares a framework dependency and no includedFrameworks, so the package would still demand an installed .NET runtime. Refusing to package it."
        }
    }
    else {
        if ($coreclr) {
            throw "coreclr.dll is present in the $rid output, so this is not a framework-dependent build."
        }
        if (-not $declaresFramework) {
            throw "The $rid runtimeconfig.json declares no framework dependency, so this is not a framework-dependent build."
        }
    }

    Write-Host ("    runtime {0} ({1})" -f $runtimeLabel, $bundledVersion)

    $files = @(Get-ChildItem -LiteralPath $outDir -Recurse -File)
    $folderMB = ($files | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("    {0} files, {1:N1} MB" -f $files.Count, $folderMB)

    $zipName = '-'
    $zipMB = $null

    if (-not $NoZip) {
        $baseName = "chroa-video-player-v$version-$archName$nameSuffix"
        $zipName = "$baseName.zip"
        $zipPath = Join-Path $releaseDir $zipName
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

        $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $files) {
                $relative = $entry.FullName.Substring($outDir.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $entry.FullName, "$baseName/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }

        $zipMB = (Get-Item -LiteralPath $zipPath).Length / 1MB
        Write-Host ("    {0}  {1:N1} MB" -f $zipName, $zipMB)
    }

    $zipMBRounded = $null
    if ($zipMB) { $zipMBRounded = [math]::Round($zipMB, 1) }

    $results += [pscustomobject]@{
        Arch     = $archName
        Rid      = $rid
        Runtime  = $runtimeLabel
        Files    = $files.Count
        FolderMB = [math]::Round($folderMB, 1)
        Zip      = $zipName
        ZipMB    = $zipMBRounded
    }
}

Write-Host ''
$results | Format-Table -AutoSize
Write-Host "version $version"
Write-Host "runtime $runtimeLabel"
Write-Host "output  $releaseDir"
if ($FrameworkDependent) {
    Write-Host "note    this build needs the .NET 10 runtime installed; end users should get the default self-contained build instead."
}
