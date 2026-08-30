[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactsDirectory,
    [string[]]$Files = @('NexusLauncher-Setup-x64.exe', 'NexusLauncher-portable-x64.zip')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$directory = (Resolve-Path -LiteralPath $ArtifactsDirectory).Path
$checksums = foreach ($file in $Files) {
    $path = Join-Path $directory $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release artifact not found: $path"
    }

    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$file"
}

$destination = Join-Path $directory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($destination, [string[]]$checksums, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $destination"
