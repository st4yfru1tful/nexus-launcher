[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '0.1.0',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [string]$ArtifactsDirectory,
    [switch]$SkipTests,
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Reset-ManagedDirectory {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Root
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $childPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedPath -eq $resolvedRoot -or -not $resolvedPath.StartsWith($childPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside the explicit artifacts directory: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'NexusLauncher.sln'
$appProject = Join-Path $repositoryRoot 'src/NexusLauncher.App/NexusLauncher.App.csproj'
$installerScript = Join-Path $repositoryRoot 'installer/NexusLauncher.iss'

if ($Version.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $Version = $Version.Substring(1)
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a semantic-version-compatible value."
}

$numericVersion = [System.Text.RegularExpressions.Regex]::Match($Version, '^\d+\.\d+\.\d+').Value
$fileVersion = "$numericVersion.0"

if (-not (Test-Path -LiteralPath $solution)) { throw "Solution not found: $solution" }
if (-not (Test-Path -LiteralPath $appProject)) { throw "App project not found: $appProject" }

if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repositoryRoot 'artifacts'
}

$artifactsDirectory = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$publishDirectory = Join-Path $artifactsDirectory 'publish'
$portableDirectory = Join-Path $artifactsDirectory 'portable'
$installerDirectory = Join-Path $artifactsDirectory 'installer'
$portableModeMarkerFileName = 'NexusLauncher.portable'

New-Item -ItemType Directory -Force -Path $artifactsDirectory | Out-Null
Reset-ManagedDirectory -Path $publishDirectory -Root $artifactsDirectory
Reset-ManagedDirectory -Path $portableDirectory -Root $artifactsDirectory
Reset-ManagedDirectory -Path $installerDirectory -Root $artifactsDirectory
foreach ($artifactName in @('NexusLauncher-Setup-x64.exe', 'NexusLauncher-portable-x64.zip', 'SHA256SUMS.txt')) {
    $artifactPath = Join-Path $artifactsDirectory $artifactName
    if (Test-Path -LiteralPath $artifactPath -PathType Leaf) {
        Remove-Item -LiteralPath $artifactPath -Force
    }
}

Push-Location $repositoryRoot
try {
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

    if (-not $SkipTests) {
        dotnet test $solution --configuration $Configuration --no-restore --logger 'trx;LogFileName=test-results.trx' --results-directory (Join-Path $artifactsDirectory 'test-results')
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    dotnet publish $appProject --configuration $Configuration --runtime $Runtime --self-contained true --output $publishDirectory `
        -p:Version=$Version `
        -p:AssemblyVersion=$fileVersion `
        -p:FileVersion=$fileVersion `
        -p:InformationalVersion=$Version `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    $portablePayload = Join-Path $portableDirectory 'NexusLauncher'
    New-Item -ItemType Directory -Force -Path $portablePayload | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $portablePayload -Recurse -Force
    Set-Content -LiteralPath (Join-Path $portablePayload $portableModeMarkerFileName) -Value 'Nexus Launcher portable mode marker v1' -Encoding ascii

    $portableArchive = Join-Path $artifactsDirectory 'NexusLauncher-portable-x64.zip'
    Compress-Archive -Path (Join-Path $portablePayload '*') -DestinationPath $portableArchive -CompressionLevel Optimal -Force

    $iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -eq $iscc) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
        if (Test-Path -LiteralPath $candidate) {
            $iscc = Get-Item -LiteralPath $candidate
        }
    }

    if ($null -eq $iscc) {
        $candidate = Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
        if (Test-Path -LiteralPath $candidate) {
            $iscc = Get-Item -LiteralPath $candidate
        }
    }

    if ((Test-Path -LiteralPath $installerScript -PathType Leaf) -and $null -ne $iscc) {
        $isccPath = if ($iscc -is [System.Management.Automation.ApplicationInfo]) { $iscc.Path } else { $iscc.FullName }
        & $isccPath "/DMyAppVersion=$Version" "/DSourceDir=$publishDirectory" "/O$installerDirectory" '/FNexusLauncher-Setup-x64' $installerScript
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

        $generatedInstaller = Join-Path $installerDirectory 'NexusLauncher-Setup-x64.exe'
        if (-not (Test-Path -LiteralPath $generatedInstaller -PathType Leaf)) {
            throw "Inno Setup did not produce the expected installer: $generatedInstaller"
        }

        Copy-Item -LiteralPath $generatedInstaller -Destination (Join-Path $artifactsDirectory 'NexusLauncher-Setup-x64.exe') -Force
    }
    elseif ($RequireInstaller) {
        if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
            throw "Installer script not found: $installerScript"
        }

        throw 'Inno Setup 6 (ISCC.exe) is required to build the installer.'
    }
    else {
        Write-Warning 'Installer was not built because Inno Setup or installer/NexusLauncher.iss is unavailable.'
    }

    $installerArtifact = Join-Path $artifactsDirectory 'NexusLauncher-Setup-x64.exe'
    if (Test-Path -LiteralPath $installerArtifact -PathType Leaf) {
        & (Join-Path $PSScriptRoot 'New-Checksums.ps1') -ArtifactsDirectory $artifactsDirectory
    }

    Write-Host "Release artifacts are in $artifactsDirectory"
}
finally {
    Pop-Location
}
