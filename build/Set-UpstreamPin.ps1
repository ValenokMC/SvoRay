param(
    [Parameter(Mandatory)][string]$From,
    [Parameter(Mandatory)][string]$To,
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$Sha256
)

# Moves the v2rayN pin to a new upstream release once its code is in src/: the bundle in
# build\cores.json and the version shown in the READMEs. Used by the upstream sync bot for a clean
# replay and after Claude resolved the conflicts of one.

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Url -notlike 'https://github.com/2dust/v2rayN/releases/download/*') {
    throw "Not an official v2rayN download: $Url"
}

$manifestPath = Join-Path $PSScriptRoot 'cores.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.v2rayN = [pscustomobject]@{ version = $To; url = $Url; sha256 = $Sha256 }
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))

foreach ($name in 'README.md', 'README_RU.md') {
    $path = Join-Path $projectRoot $name
    $bytes = [IO.File]::ReadAllBytes($path)
    # README_RU.md starts with a BOM and README.md does not; each keeps what it has.
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($path)
    # Only the version that follows a mention of v2rayN on the same line (text, badge, link).
    $text = [regex]::Replace($text, '(?<=v2rayN[^\r\n]{0,80}?)' + [regex]::Escape($From), $To)
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($hasBom))
}
