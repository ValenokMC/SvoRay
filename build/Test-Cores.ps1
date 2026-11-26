param(
    # Folder holding xray\xray.exe, sing_box\sing-box.exe and the geodata (a publish "bin" folder).
    [Parameter(Mandatory)][string]$BinDir,
    # Folder of configs written by CoreFixtureConfigTests (SVORAY_CORE_FIXTURES).
    [Parameter(Mandatory)][string]$Fixtures
)

# Runs every config SvoRay generates through the cores that will ship with it:
# "xray run -test" for xray-*.json and "sing-box check" for singbox-*.json.
# Fails on the first config a core rejects, so a core that changed its config format never
# reaches a release.

$ErrorActionPreference = 'Stop'
$xray = Join-Path $BinDir 'xray\xray.exe'
$singbox = Join-Path $BinDir 'sing_box\sing-box.exe'
foreach ($exe in @($xray, $singbox)) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Core not found: $exe" }
}
$files = Get-ChildItem -LiteralPath $Fixtures -Filter '*.json' -File
if (-not ($files | Where-Object Name -like 'xray-*') -or -not ($files | Where-Object Name -like 'singbox-*')) {
    throw "No generated configs in $Fixtures. Run the tests with SVORAY_CORE_FIXTURES set first."
}

$env:XRAY_LOCATION_ASSET = $BinDir
# The cores log warnings to stderr. Windows PowerShell turns redirected stderr into error records,
# which 'Stop' would throw on: only the exit code and the output text decide here.
$ErrorActionPreference = 'Continue'
$failed = @()
foreach ($file in $files) {
    if ($file.Name -like 'xray-*') {
        $output = & $xray run -test -c $file.FullName 2>&1 | ForEach-Object { "$_" } | Out-String
        $ok = $LASTEXITCODE -eq 0 -and $output -match 'Configuration OK'
    }
    else {
        $output = & $singbox check -c $file.FullName -D $BinDir 2>&1 | ForEach-Object { "$_" } | Out-String
        $ok = $LASTEXITCODE -eq 0
    }
    $warn = ($output -split "`n" | Where-Object { $_ -match '(?i)deprecat|\[Warning\]|WARN' }) -join ' | '
    if ($ok) {
        Write-Host "ok    $($file.Name)$(if ($warn) { "  ($warn)" })"
    }
    else {
        Write-Host "FAIL  $($file.Name)`n$output"
        $failed += $file.Name
    }
}

$ErrorActionPreference = 'Stop'
Write-Host ''
Write-Host (& $xray version | Select-Object -First 1)
Write-Host (& $singbox version | Select-Object -First 1)
if ($failed.Count -gt 0) {
    throw "$($failed.Count) of $($files.Count) generated configs were rejected: $($failed -join ', ')"
}
Write-Host "All $($files.Count) generated configs accepted."
