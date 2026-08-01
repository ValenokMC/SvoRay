param(
    [string]$CoreSource = "$env:LOCALAPPDATA\Programs\v2rayN\v2rayN-windows-64",
    [string]$Version = '0.2.0',
    [string]$Runtime = 'win-x64',
    [switch]$SkipPublish
)

# Produces the single downloadable SvoRay-<version>-setup.exe from a clean
# publish tree, then writes SHA256SUMS.txt for every release artifact.

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = Join-Path $projectRoot 'dist'
$appDirectory = Join-Path $distRoot "SvoRay-$Version-$Runtime"

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'Publish.ps1') -CoreSource $CoreSource -Version $Version -Runtime $Runtime | Out-Null
}

if (-not (Test-Path -LiteralPath $appDirectory -PathType Container)) {
    throw "Publish output was not found: $appDirectory"
}

# The publish script already refuses to package generated state; re-check here so a
# stale directory can never reach the installer.
foreach ($forbidden in @('guiConfigs', 'guiLogs', 'guiTemps', 'guiBackups', 'binConfigs')) {
    $path = Join-Path $appDirectory $forbidden
    if (Test-Path -LiteralPath $path) {
        throw "Private or generated data would enter the installer: $path"
    }
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup'
}

$script = Join-Path $PSScriptRoot 'SvoRay.iss'
& $iscc "/DAppVersion=$Version" "/DSourceDir=$appDirectory" "/DOutputDir=$distRoot" $script | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

$setupExe = Join-Path $distRoot "SvoRay-$Version-setup.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer was not produced: $setupExe"
}

$artifacts = @(
    $setupExe,
    (Join-Path $distRoot "SvoRay-$Version-$Runtime.zip"),
    (Join-Path $distRoot "SvoRay-$Version-source.zip")
) | Where-Object { Test-Path -LiteralPath $_ }

$sumsPath = Join-Path $distRoot 'SHA256SUMS.txt'
$lines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    "$hash  $([System.IO.Path]::GetFileName($artifact))"
}
Set-Content -LiteralPath $sumsPath -Value $lines -Encoding utf8

foreach ($artifact in $artifacts) {
    [pscustomobject]@{
        Artifact = $artifact
        SHA256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
        SizeMB = [math]::Round((Get-Item -LiteralPath $artifact).Length / 1MB, 2)
    }
}
