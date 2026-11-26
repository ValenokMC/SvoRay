param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Runtime = 'win-x64'
)

# The gate every automatic release goes through, and the one to run by hand before a release:
#   1. all tests, writing the configs SvoRay generates for each protocol (CoreFixtureConfigTests);
#   2. a release publish with the cores pinned in build\cores.json (downloaded, SHA-256 checked);
#   3. those configs through the very cores that publish ships (Test-Cores.ps1).
# Leaves dist\SvoRay-<version>-<runtime> ready for BuildInstaller.ps1 -SkipPublish.

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = Join-Path $projectRoot 'dist'
$fixtures = Join-Path $distRoot '.fixtures'
$testBin = Join-Path $projectRoot 'src\ServiceLib.Tests\bin\Release'

# The tests write the configs to core-fixtures next to their assembly (CoreFixtureConfigTests).
# Old ones go first, so a protocol that was dropped cannot pass with a stale file.
foreach ($stale in @(Get-ChildItem -LiteralPath $testBin -Directory -Filter 'core-fixtures' -Recurse -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $stale.FullName -Recurse -Force
}
if (Test-Path -LiteralPath $fixtures) { Remove-Item -LiteralPath $fixtures -Recurse -Force }

$env:SVORAY_CORE_FIXTURES = '1'
# dotnet writes progress to stderr, which Windows PowerShell would turn into a terminating error
# under 'Stop': the exit code decides instead.
$ErrorActionPreference = 'Continue'
try {
    dotnet test (Join-Path $projectRoot 'src\ServiceLib.Tests\ServiceLib.Tests.csproj') --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    Remove-Item Env:SVORAY_CORE_FIXTURES -ErrorAction SilentlyContinue
    $ErrorActionPreference = 'Stop'
}

$written = @(Get-ChildItem -LiteralPath $testBin -Directory -Filter 'core-fixtures' -Recurse)
if ($written.Count -ne 1) {
    throw "Expected one core-fixtures folder under $testBin, found $($written.Count)."
}
Copy-Item -LiteralPath $written[0].FullName -Destination $fixtures -Recurse

& (Join-Path $PSScriptRoot 'Publish.ps1') -Version $Version -Runtime $Runtime | Out-Null

& (Join-Path $PSScriptRoot 'Test-Cores.ps1') -BinDir (Join-Path $distRoot "SvoRay-$Version-$Runtime\bin") -Fixtures $fixtures
