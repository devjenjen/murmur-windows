[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Tag,

    [Parameter(Mandatory)]
    [ValidateSet('beta', 'stable')]
    [string] $Channel,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PackageListPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputPath,

    [string] $RepositoryDirectory = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail
{
    param([Parameter(Mandatory)][string] $Message)
    throw "Release build-info generation failed: $Message"
}

$resolver = Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1'
$resolved = & $resolver -Tag $Tag -RepositoryDirectory $RepositoryDirectory
$packagePath = [IO.Path]::GetFullPath($PackageListPath)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { Fail "Package list missing: '$packagePath'." }
try { $packages = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json -Depth 100 }
catch { Fail "Package list is invalid JSON: '$packagePath'." }

$models = @(
    [ordered]@{
        id = 'parakeet-v3'
        archiveSize = 487170055
        archiveSha256 = '5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf'
    },
    [ordered]@{
        id = 'nemotron-3.5-streaming-160ms'
        archiveSize = 475273363
        archiveSha256 = 'a81909a1780d84cff16d73c15e13e67d9d81d8839faf14870d507d8499f7a61a'
    }
)

$status = @(& git -C $RepositoryDirectory status --porcelain 2>&1)
if ($LASTEXITCODE -ne 0) { Fail 'Could not inspect Git dirty state.' }
$output = [ordered]@{
    tag = $resolved.Tag
    version = $resolved.Version
    sourceSha = $resolved.SourceSha
    dirty = @($status).Count -gt 0
    channel = $Channel
    sdk = (& dotnet --version).Trim()
    os = [Environment]::OSVersion.VersionString
    rid = 'win-x64'
    packages = $packages
    models = $models
}

$outputFile = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFile
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $outputFile -Encoding utf8NoBOM
Write-Output "Build info written: $outputFile"
