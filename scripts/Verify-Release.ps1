[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Windows PowerShell 5.1 does not automatically load the assembly that exposes
# ZipFile. Load it explicitly while remaining a no-op on modern PowerShell.
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$directory = (Resolve-Path -LiteralPath $ArtifactsDirectory).Path
$required = @(
    'NexusLauncher-Setup-x64.exe',
    'NexusLauncher-portable-x64.zip',
    'SHA256SUMS.txt'
)

foreach ($file in $required) {
    $path = Join-Path $directory $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release artifact is missing: $path"
    }
}

$checksumFile = Join-Path $directory 'SHA256SUMS.txt'
$expectedNames = @('NexusLauncher-Setup-x64.exe', 'NexusLauncher-portable-x64.zip')
$checksumEntries = Get-Content -LiteralPath $checksumFile |
    Where-Object { $_ -match '^[a-fA-F0-9]{64} \*.+$' } |
    ForEach-Object {
        $parts = $_ -split ' \*', 2
        [pscustomobject]@{ Hash = $parts[0]; Name = $parts[1] }
    }

if ($checksumEntries.Count -ne $expectedNames.Count -or
    @($checksumEntries.Name | Sort-Object -Unique).Count -ne $expectedNames.Count -or
    ((@($checksumEntries.Name | Sort-Object) -join "`n") -ne (@($expectedNames | Sort-Object) -join "`n"))) {
    throw 'SHA256SUMS.txt must contain exactly one SHA-256 entry for each expected distributable.'
}

foreach ($entry in $checksumEntries) {
    $parts = @($entry.Hash, $entry.Name)
    $actual = (Get-FileHash -LiteralPath (Join-Path $directory $parts[1]) -Algorithm SHA256).Hash
    if ($actual -ne $parts[0]) {
        throw "Checksum mismatch for $($parts[1])."
    }
}

$installer = Join-Path $directory 'NexusLauncher-Setup-x64.exe'
$installerVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
$installerProductVersion = $installerVersionInfo.ProductVersion.Trim()
$installerFileVersion = $installerVersionInfo.FileVersion.Trim()
if ($installerProductVersion -notmatch '^(\d+\.\d+\.\d+)(?:[-+].*)?$') {
    throw "Installer product version '$($installerVersionInfo.ProductVersion)' is not a semantic version."
}

$expectedInstallerFileVersion = "$($Matches[1]).0"
if ($installerFileVersion -ne $expectedInstallerFileVersion) {
    throw "Installer file version '$($installerVersionInfo.FileVersion)' does not match product version '$($installerVersionInfo.ProductVersion)'."
}

$archive = Join-Path $directory 'NexusLauncher-portable-x64.zip'
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $archiveEntries = $zip.Entries
    if ($archiveEntries.Count -eq 0) {
        throw 'The portable archive is empty.'
    }

    if (-not ($archiveEntries.FullName -contains 'NexusLauncher.exe')) {
        throw 'The portable archive does not contain NexusLauncher.exe at its root.'
    }

    if (-not ($archiveEntries.FullName -contains 'NexusLauncher.portable')) {
        throw 'The portable archive does not contain the NexusLauncher.portable mode marker.'
    }

    foreach ($notice in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
        if (-not ($archiveEntries.FullName -contains $notice)) {
            throw "The portable archive does not contain $notice at its root."
        }
    }
}
finally {
    $zip.Dispose()
}

Write-Host 'Release artifacts and checksums verified.'
