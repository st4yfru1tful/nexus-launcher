[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'NexusLauncher.sln'

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found: $solution"
}

Push-Location $repositoryRoot
try {
    if (-not $SkipRestore) {
        dotnet restore $solution
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }

    dotnet build $solution --configuration $Configuration --no-restore -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    $resultsDirectory = Join-Path $repositoryRoot 'TestResults'
    dotnet test $solution --configuration $Configuration --no-build --logger 'trx;LogFileName=test-results.trx' --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    Pop-Location
}
