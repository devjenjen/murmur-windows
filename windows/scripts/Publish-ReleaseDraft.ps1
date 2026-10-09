[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Tag,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Repository,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $AssetsDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $NotesPath,

    [Parameter(Mandatory)]
    [bool] $Prerelease,

    [string] $RepositoryDirectory = (Get-Location).Path,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail
{
    param([Parameter(Mandatory)][string] $Message)
    throw "Release draft publication failed: $Message"
}

$resolver = Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1'
$resolved = & $resolver -Tag $Tag -RepositoryDirectory $RepositoryDirectory
if ($null -eq $resolved -or @($resolved).Count -ne 1)
{
    Fail 'Release version resolver returned no unique result.'
}
$version = [string]$resolved.Version
$assetsRoot = [IO.Path]::GetFullPath($AssetsDirectory)
$notesFile = [IO.Path]::GetFullPath($NotesPath)
if (-not (Test-Path -LiteralPath $assetsRoot -PathType Container)) { Fail "Assets directory missing: '$assetsRoot'." }
if (-not (Test-Path -LiteralPath $notesFile -PathType Leaf)) { Fail "Release notes missing: '$notesFile'." }

$assetNames = @(
    "Murmur-$version-win-x64.zip",
    "Murmur-Setup-v$version.exe",
    'build-info.json',
    'SHA256SUMS.txt'
)
$assetPaths = foreach ($assetName in $assetNames)
{
    $relativeCandidates = if ($assetName -like 'Murmur-Setup-v*.exe')
    {
        @($assetName, (Join-Path 'installer' $assetName))
    }
    else
    {
        @($assetName)
    }

    $assetPath = $null
    foreach ($relativeCandidate in $relativeCandidates)
    {
        $candidatePath = [IO.Path]::GetFullPath((Join-Path $assetsRoot $relativeCandidate))
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf)
        {
            $assetPath = $candidatePath
            break
        }
    }

    if ($null -eq $assetPath)
    {
        Fail "Expected release asset missing in root or installer subdirectory: '$assetName'."
    }

    $assetPath
}

$buildInfoPath = Join-Path $assetsRoot 'build-info.json'
try { $buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json }
catch { Fail "build-info.json is invalid: '$buildInfoPath'." }
if ($buildInfo.tag -ne $Tag -or $buildInfo.version -ne $version -or
    -not [string]::Equals([string]$buildInfo.sourceSha, [string]$resolved.SourceSha, [StringComparison]::OrdinalIgnoreCase))
{
    Fail 'build-info.json does not match the resolved tag, version, and source SHA.'
}

$releaseArguments = @(
    'release', 'create', $Tag,
    '--repo', $Repository, '--verify-tag', '--draft', '--latest=false',
    '--title', "Murmur $version", '--notes-file', $notesFile
)
if ($Prerelease) { $releaseArguments += '--prerelease' }
$releaseArguments += $assetPaths

if ($DryRun)
{
    [pscustomobject]@{
        Tag = $Tag
        Version = $version
        SourceSha = $resolved.SourceSha
        Prerelease = $Prerelease
        CreateArguments = $releaseArguments
        AssetPaths = $assetPaths
    } | ConvertTo-Json -Depth 5
    return
}

$viewArguments = @('release', 'view', $Tag, '--repo', $Repository, '--json', 'tagName,isDraft,isPrerelease')
$viewOutput = @(& gh @viewArguments 2>&1)
$viewExitCode = $LASTEXITCODE
if ($viewExitCode -eq 0)
{
    try { $existing = ($viewOutput -join "`n") | ConvertFrom-Json }
    catch { Fail "GitHub returned invalid release metadata for '$Tag'." }
    if (-not [bool]$existing.isDraft)
    {
        Fail "Release '$Tag' is already published; refusing to overwrite it."
    }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('murmur-release-draft-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try
    {
        $downloadArguments = @('release', 'download', $Tag, '--repo', $Repository, '--pattern', 'build-info.json', '--dir', $tempRoot, '--clobber')
        & gh @downloadArguments 1> $null
        if ($LASTEXITCODE -ne 0) { Fail "Existing draft '$Tag' has no downloadable build-info.json for verification." }
        $remoteBuildInfoPath = Join-Path $tempRoot 'build-info.json'
        if (-not (Test-Path -LiteralPath $remoteBuildInfoPath -PathType Leaf)) { Fail "Existing draft '$Tag' did not provide build-info.json." }
        $remoteBuildInfo = Get-Content -LiteralPath $remoteBuildInfoPath -Raw | ConvertFrom-Json
        if ($remoteBuildInfo.tag -ne $Tag -or $remoteBuildInfo.version -ne $version -or
            -not [string]::Equals([string]$remoteBuildInfo.sourceSha, [string]$resolved.SourceSha, [StringComparison]::OrdinalIgnoreCase))
        {
            Fail "Existing draft '$Tag' has a different tag, version, or source SHA."
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    }

    $editArguments = @('release', 'edit', $Tag, '--repo', $Repository, '--title', "Murmur $version", '--notes-file', $notesFile)
    $editArguments += if ($Prerelease) { '--prerelease' } else { '--prerelease=false' }
    & gh @editArguments
    if ($LASTEXITCODE -ne 0) { Fail "Updating draft release '$Tag' failed." }
    $uploadArguments = @('release', 'upload', $Tag) + $assetPaths + @('--repo', $Repository, '--clobber')
    & gh @uploadArguments
    if ($LASTEXITCODE -ne 0) { Fail "Uploading assets to draft release '$Tag' failed." }
    Write-Output "Updated verified draft release '$Tag'."
    return
}

$viewText = $viewOutput -join ' '
if ($viewText -notmatch '(?im)(?:HTTP(?:/\d(?:\.\d+)?)?\s+404\b|release not found|not found)')
{
    Fail "Unable to inspect release '$Tag': $viewText"
}

& gh @releaseArguments
if ($LASTEXITCODE -ne 0) { Fail "Draft release creation failed for '$Tag'." }
Write-Output "Created verified draft release '$Tag'."
