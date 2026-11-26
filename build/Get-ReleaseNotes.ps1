param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile
)

# Release notes for SvoRay <version>: docs\RELEASE_NOTES_<version>_RU.md when someone wrote one,
# otherwise that version's CHANGELOG.md section (or "Unreleased" for a release made by hand).

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$written = Join-Path $projectRoot "docs\RELEASE_NOTES_$($Version)_RU.md"
if (Test-Path -LiteralPath $written -PathType Leaf) {
    Copy-Item -LiteralPath $written -Destination $OutFile -Force
    return
}

$text = (Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw -Encoding utf8) -replace "`r`n", "`n"
$body = $null
foreach ($pattern in @("(?m)^## $([regex]::Escape($Version))\b.*$", '(?m)^## Unreleased[ \t]*$')) {
    $start = [regex]::Match($text, $pattern)
    if ($start.Success) {
        $rest = $text.Substring($start.Index + $start.Length)
        $next = [regex]::Match($rest, '(?m)^## ')
        $body = if ($next.Success) { $rest.Substring(0, $next.Index) } else { $rest }
        break
    }
}
if ([string]::IsNullOrWhiteSpace($body)) {
    $body = "`nCore and maintenance update."
}

$notes = "# SvoRay $Version`n`n" + $body.Trim() + "`n`n" +
    "SvoRay offers this update by itself. Installer checksums: ``SHA256SUMS.txt``.`n"
Set-Content -LiteralPath $OutFile -Value $notes -Encoding utf8 -NoNewline
