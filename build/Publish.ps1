param(
    [string]$CoreSource = "$env:LOCALAPPDATA\Programs\v2rayN\v2rayN-windows-64",
    [string]$Version = '0.3.0',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = Join-Path $projectRoot 'dist'
$appName = "SvoRay-$Version-$Runtime"
$appDirectory = Join-Path $distRoot $appName
$binaryZip = Join-Path $distRoot "$appName.zip"
$sourceZip = Join-Path $distRoot "SvoRay-$Version-source.zip"
$sourceStage = Join-Path $distRoot '.source-stage'

function Assert-SafeDistPath {
    param([string]$Path)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $distPrefix = [System.IO.Path]::GetFullPath($distRoot) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside dist: $resolved"
    }
}

function Reset-Directory {
    param([string]$Path)
    Assert-SafeDistPath $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-CleanSourceTree {
    param([string]$Source, [string]$Destination)
    # Keep this list aligned with .gitignore: the source archive must contain exactly what
    # the public repository contains. Anything the client can create next to itself may
    # hold a real subscription, and 'internal' holds unpublished working notes.
    $skipDirectories = @('bin', 'obj', 'dist', '.git', '.vs', 'internal', 'guiConfigs', 'guiLogs', 'guiTemps', 'guiBackups', 'guiFonts', 'binConfigs')
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        if ($entry.PSIsContainer -and $skipDirectories -contains $entry.Name) {
            continue
        }
        $target = Join-Path $Destination $entry.Name
        if ($entry.PSIsContainer) {
            Copy-CleanSourceTree $entry.FullName $target
        }
        else {
            Copy-Item -LiteralPath $entry.FullName -Destination $target -Force
        }
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
Reset-Directory $appDirectory
$clientProject = Join-Path $projectRoot 'src\v2rayN\v2rayN.csproj'

dotnet publish $clientProject -c Release -r $Runtime --self-contained true -o $appDirectory
if ($LASTEXITCODE -ne 0) { throw 'SvoRay publish failed.' }

$coreRoot = [System.IO.Path]::GetFullPath($CoreSource)
$binSource = Join-Path $coreRoot 'bin'
if (-not (Test-Path -LiteralPath $binSource -PathType Container)) {
    throw "Official v2rayN core folder was not found: $binSource"
}

$amazToolSource = Join-Path $coreRoot 'AmazTool.exe'
if (-not (Test-Path -LiteralPath $amazToolSource -PathType Leaf)) {
    throw "Required v2rayN helper is missing: $amazToolSource"
}
Copy-Item -LiteralPath $amazToolSource -Destination (Join-Path $appDirectory 'AmazTool.exe') -Force

$binDestination = Join-Path $appDirectory 'bin'
New-Item -ItemType Directory -Path $binDestination -Force | Out-Null

$runtimeFiles = @(
    'Country.mmdb',
    'EnableLoopback.exe',
    'geoip-only-cn-private.dat',
    'geoip.dat',
    'geoip.metadb',
    'geosite.dat'
)
foreach ($name in $runtimeFiles) {
    $source = Join-Path $binSource $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $binDestination $name) -Force
    }
}

foreach ($directoryName in @('xray', 'sing_box', 'srss')) {
    $source = Join-Path $binSource $directoryName
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required runtime component is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination $binDestination -Recurse -Force
}

foreach ($document in @('LICENSE', 'README.md', 'README_RU.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination (Join-Path $appDirectory $document) -Force
}

$forbiddenPaths = @(
    (Join-Path $appDirectory 'guiConfigs'),
    (Join-Path $appDirectory 'guiLogs'),
    (Join-Path $appDirectory 'guiTemps'),
    (Join-Path $appDirectory 'binConfigs'),
    (Join-Path $appDirectory 'bin\cache.db')
)
foreach ($forbiddenPath in $forbiddenPaths) {
    if (Test-Path -LiteralPath $forbiddenPath) {
        throw "Private or generated data entered the release: $forbiddenPath"
    }
}

foreach ($zipPath in @($binaryZip, $sourceZip)) {
    Assert-SafeDistPath $zipPath
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
}

Compress-Archive -Path (Join-Path $appDirectory '*') -DestinationPath $binaryZip -CompressionLevel Optimal

Reset-Directory $sourceStage
Copy-CleanSourceTree $projectRoot $sourceStage

# The skip list works on directory names only, so re-check the staged tree for the file
# types that carry user data even when they sit somewhere unexpected.
$strayFiles = Get-ChildItem -LiteralPath $sourceStage -Recurse -File -Force |
    Where-Object { $_.Extension -in @('.db', '.log') -or $_.Name -eq 'guiNConfig.json' }
if ($strayFiles) {
    throw "Private or generated data entered the source archive: $(($strayFiles | ForEach-Object FullName) -join ', ')"
}

Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -CompressionLevel Optimal
Remove-Item -LiteralPath $sourceStage -Recurse -Force

$binaryHash = (Get-FileHash -LiteralPath $binaryZip -Algorithm SHA256).Hash
$sourceHash = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash

[pscustomobject]@{
    Binary = $binaryZip
    BinarySHA256 = $binaryHash
    Source = $sourceZip
    SourceSHA256 = $sourceHash
}
