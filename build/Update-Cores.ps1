param(
    # The SvoRay version the bump will be released as; its CHANGELOG section gets the core lines.
    [Parameter(Mandatory)][string]$ReleaseVersion,
    # A release has to have stood as "latest" this long before it is taken: Xray has pulled
    # releases within days (v26.10.10, v26.10.17, v26.10.29), and none of those should ship.
    [int]$MinAgeHours = 48
)

# Looks for newer stable Xray and sing-box releases than the ones pinned in build\cores.json.
# For each one found, it writes the new version, download and the SHA-256 GitHub reports for the
# asset into cores.json and adds a line to CHANGELOG.md. Nothing is downloaded here: Publish.ps1
# fetches and checks the archives, Verify-Build.ps1 decides whether they ship.
# In GitHub Actions it sets the outputs "changed" (true/false) and "summary".

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'cores.json'
$changelogPath = Join-Path $projectRoot 'CHANGELOG.md'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$headers = @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'svoray-core-update' }
if ($env:GH_TOKEN) { $headers['Authorization'] = "Bearer $env:GH_TOKEN" }

function Get-LatestPin {
    param([string]$Repo, [scriptblock]$AssetName)
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
    if ($release.draft -or $release.prerelease) { return $null }
    $version = $release.tag_name.TrimStart('v')
    if ($version -notmatch '^\d+\.\d+\.\d+$') { return $null }
    # pwsh turns ISO dates in JSON into DateTime, Windows PowerShell keeps the string: a cast takes both.
    $age = [DateTimeOffset]::UtcNow - [DateTimeOffset]$release.published_at
    if ($age.TotalHours -lt $MinAgeHours) {
        Write-Host "$Repo $version is $([int]$age.TotalHours)h old; waiting for $MinAgeHours h."
        return $null
    }
    $name = & $AssetName $version
    $asset = $release.assets | Where-Object name -eq $name | Select-Object -First 1
    if (-not $asset -or "$($asset.digest)" -notmatch '^sha256:([0-9a-f]{64})$') {
        Write-Host "$Repo $version has no $name with a SHA-256 digest; skipped."
        return $null
    }
    [pscustomobject]@{ version = $version; url = $asset.browser_download_url; sha256 = $Matches[1] }
}

$sources = @(
    @{ Key = 'xray'; Title = 'Xray'; Repo = 'XTLS/Xray-core'; Asset = { param($v) 'Xray-windows-64.zip' } },
    @{ Key = 'singbox'; Title = 'sing-box'; Repo = 'SagerNet/sing-box'; Asset = { param($v) "sing-box-$v-windows-amd64.zip" } }
)

$lines = @()
foreach ($source in $sources) {
    $current = $manifest.($source.Key)
    $latest = Get-LatestPin $source.Repo $source.Asset
    if (-not $latest -or [version]$latest.version -le [version]$current.version) {
        Write-Host "$($source.Title): $($current.version) is current."
        continue
    }
    Write-Host "$($source.Title): $($current.version) -> $($latest.version)"
    $lines += "- **$($source.Title) $($latest.version)** (was $($current.version)): " +
        "https://github.com/$($source.Repo)/releases/tag/v$($latest.version)"
    $manifest.($source.Key) = $latest
}

$changed = $lines.Count -gt 0
if ($changed) {
    # Written through .NET so Windows PowerShell and pwsh give the same bytes: cores.json without
    # a BOM, CHANGELOG.md keeps the one it has.
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))

    # The bump is released at once, so the lines go straight into that release's section. Notes
    # written under "Unreleased" by hand ship with it and move under the same heading.
    $date = [DateTime]::UtcNow.ToString('d MMMM yyyy', [Globalization.CultureInfo]::InvariantCulture)
    $heading = "## $ReleaseVersion $([char]0x2014) $date"
    $block = "### Cores`n`nUpdated automatically after the generated configs passed the new cores.`n`n" + ($lines -join "`n") + "`n"
    $text = (Get-Content -LiteralPath $changelogPath -Raw -Encoding utf8) -replace "`r`n", "`n"
    $unreleased = [regex]::Match($text, '(?m)^## Unreleased[ \t]*$')
    if ($unreleased.Success) {
        $next = [regex]::Match($text.Substring($unreleased.Index + $unreleased.Length), '(?m)^## ')
        $end = if ($next.Success) { $unreleased.Index + $unreleased.Length + $next.Index } else { $text.Length }
        $section = $text.Substring($unreleased.Index, $end - $unreleased.Index).TrimEnd()
        $section = $heading + $section.Substring($unreleased.Length)
        $text = $text.Substring(0, $unreleased.Index) + $section + "`n`n" + $block + "`n" + $text.Substring($end)
    }
    else {
        $first = [regex]::Match($text, '(?m)^## ')
        $text = $text.Substring(0, $first.Index) + "$heading`n`n$block`n" + $text.Substring($first.Index)
    }
    # Back to the line endings the checkout uses (CRLF on Windows), so only the new lines differ.
    if ((Get-Content -LiteralPath $changelogPath -Raw -Encoding utf8) -match "`r`n") { $text = $text -replace "`n", "`r`n" }
    [IO.File]::WriteAllText($changelogPath, $text, [Text.UTF8Encoding]::new($true))
}

if ($env:GITHUB_OUTPUT) {
    "changed=$($changed.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    $summary = ($lines | ForEach-Object { ($_ -replace '\*\*', '') -replace ':\s+https\S+', '' }) -join '; '
    "summary=$summary" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
