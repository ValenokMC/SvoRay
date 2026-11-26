param(
    # An unpacked official v2rayN-windows-64 release. Empty: the one pinned in build\cores.json is
    # downloaded and checked (what CI does). Xray and sing-box always come from cores.json.
    [string]$CoreSource = '',
    [string]$Version = '0.6.4',
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
    # The source archive must contain only the public project. Client-generated folders may
    # hold a real subscription, while local assistant workspaces and 'internal' hold
    # unpublished working notes even when a developer has not ignored them globally.
    $skipDirectories = @('bin', 'obj', 'dist', '.git', '.vs', '.claude', 'internal', 'guiConfigs', 'guiLogs', 'guiTemps', 'guiBackups', 'guiFonts', 'binConfigs')
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

# Every core and helper SvoRay ships is pinned in build\cores.json: a version, the official
# download and its SHA-256. A bump is a reviewed one-line change (by hand or by the core update
# bot), and a build never ships whatever happens to sit on the machine.
$coreManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cores.json') -Raw | ConvertFrom-Json
$coreCache = Join-Path $distRoot '.cores'

function Get-PinnedArchive {
    param([string]$Name, $Pin)
    New-Item -ItemType Directory -Path $coreCache -Force | Out-Null
    $zip = Join-Path $coreCache "$Name-$($Pin.version).zip"
    if (-not (Test-Path -LiteralPath $zip -PathType Leaf) -or
        (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $Pin.sha256) {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $Pin.url -OutFile $zip -UseBasicParsing
    }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($hash -ne $Pin.sha256) {
        Remove-Item -LiteralPath $zip -Force
        throw "$Name $($Pin.version) archive does not match the pinned SHA-256 (got $hash)."
    }
    $stage = Join-Path $coreCache "$Name-$($Pin.version)"
    Reset-Directory $stage
    Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
    # Archives that wrap everything in one top folder (v2rayN, sing-box) are unwrapped.
    $entries = @(Get-ChildItem -LiteralPath $stage -Force)
    if ($entries.Count -eq 1 -and $entries[0].PSIsContainer) {
        return $entries[0].FullName
    }
    return $stage
}

dotnet publish $clientProject -c Release -r $Runtime --self-contained true -o $appDirectory -p:Version=$Version -p:InformationalVersion="$Version+v2rayN.$($coreManifest.v2rayN.version)"
if ($LASTEXITCODE -ne 0) { throw 'SvoRay publish failed.' }

if ([string]::IsNullOrWhiteSpace($CoreSource)) {
    $CoreSource = Get-PinnedArchive 'v2rayN' $coreManifest.v2rayN
}
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

# Xray and sing-box over the ones the v2rayN bundle carries, each checked by the version the
# binary itself reports.
$overlays = @(
    @{ Name = 'xray'; Pin = $coreManifest.xray; Folder = 'xray'; Files = @('xray.exe', 'wintun.dll');
       Exe = 'xray.exe'; VersionArgs = @('version'); Expect = "Xray $($coreManifest.xray.version) *" },
    @{ Name = 'sing-box'; Pin = $coreManifest.singbox; Folder = 'sing_box'; Files = @('sing-box.exe', 'libcronet.dll');
       Exe = 'sing-box.exe'; VersionArgs = @('version'); Expect = "sing-box version $($coreManifest.singbox.version)" }
)
foreach ($overlay in $overlays) {
    $source = Get-PinnedArchive $overlay.Name $overlay.Pin
    $destination = Join-Path $binDestination $overlay.Folder
    foreach ($name in $overlay.Files) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name) -Force
    }
    $reported = (& (Join-Path $destination $overlay.Exe) @($overlay.VersionArgs) | Select-Object -First 1)
    if ($reported -notlike $overlay.Expect) {
        throw "Bundled $($overlay.Exe) reports '$reported', expected $($overlay.Pin.version)."
    }
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
