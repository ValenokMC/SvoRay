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

if (Test-Path -LiteralPath $fixtures) { Remove-Item -LiteralPath $fixtures -Recurse -Force }
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null

$env:SVORAY_CORE_FIXTURES = $fixtures
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

& (Join-Path $PSScriptRoot 'Publish.ps1') -Version $Version -Runtime $Runtime | Out-Null

& (Join-Path $PSScriptRoot 'Test-Cores.ps1') -BinDir (Join-Path $distRoot "SvoRay-$Version-$Runtime\bin") -Fixtures $fixtures
