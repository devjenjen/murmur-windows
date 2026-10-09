[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
$resolver = Join-Path $scriptDirectory 'Resolve-ReleaseVersion.ps1'
$publisher = Join-Path $scriptDirectory 'Publish-ReleaseDraft.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('murmur-release-automation-' + [Guid]::NewGuid().ToString('N'))
$repo = Join-Path $testRoot 'repo'
$assets = Join-Path $testRoot 'assets'
$notes = Join-Path $testRoot 'release-notes.md'

function Assert-Fails
{
    param([Parameter(Mandatory)][scriptblock] $Action, [Parameter(Mandatory)][string] $Description)
    $failed = $false
    try { & $Action *> $null }
    catch { $failed = $true }
    if (-not $failed) { throw "$Description should fail." }
}

function Invoke-Git
{
    param([Parameter(Mandatory)][string[]] $Arguments)
    & git -C $repo @Arguments 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
}

try
{
    New-Item -ItemType Directory -Path $repo, $assets -Force | Out-Null
    Invoke-Git @('init', '--initial-branch=main')
    Invoke-Git @('config', 'user.email', 'release-test@example.invalid')
    Invoke-Git @('config', 'user.name', 'Release Test')
    Set-Content -LiteralPath (Join-Path $repo 'source.txt') -Value 'release test' -NoNewline
    Invoke-Git @('add', 'source.txt')
    Invoke-Git @('commit', '-m', 'release test')
    Invoke-Git @('tag', 'v0.2.1')

    $resolved = & $resolver -Tag 'v0.2.1' -RepositoryDirectory $repo
    if ($resolved.Tag -ne 'v0.2.1' -or $resolved.Version -ne '0.2.1' -or $resolved.SourceSha -notmatch '^[0-9a-f]{40}$')
    {
        throw 'Valid numeric tag did not resolve to the expected object.'
    }

    foreach ($invalidTag in @('0.2.1', 'v0.2.1-beta.1', "v0.2.1`n--repo=evil", 'v0.2.1 --repo evil'))
    {
        Assert-Fails { & $resolver -Tag $invalidTag -RepositoryDirectory $repo } "Invalid tag '$invalidTag'"
    }

    Set-Content -LiteralPath $notes -Value '# Release notes' -NoNewline
    New-Item -ItemType Directory -Path (Join-Path $assets 'installer') -Force | Out-Null
    foreach ($assetName in @('Murmur-0.2.1-win-x64.zip', 'SHA256SUMS.txt'))
    {
        Set-Content -LiteralPath (Join-Path $assets $assetName) -Value $assetName -NoNewline
    }
    Set-Content -LiteralPath (Join-Path $assets 'installer\Murmur-Setup-v0.2.1.exe') -Value 'installer' -NoNewline
    @{ tag = 'v0.2.1'; version = '0.2.1'; sourceSha = $resolved.SourceSha } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $assets 'build-info.json') -Encoding utf8NoBOM

    $betaPreview = & $publisher -Tag 'v0.2.1' -Repository 'owner/repo' -AssetsDirectory $assets -NotesPath $notes -Prerelease $true -RepositoryDirectory $repo -DryRun | ConvertFrom-Json
    $betaArguments = @($betaPreview.CreateArguments)
    foreach ($required in @('--draft', '--prerelease', '--latest=false'))
    {
        if ($betaArguments -notcontains $required) { throw "Beta preview misses '$required'." }
    }

    $stablePreview = & $publisher -Tag 'v0.2.1' -Repository 'owner/repo' -AssetsDirectory $assets -NotesPath $notes -Prerelease $false -RepositoryDirectory $repo -DryRun | ConvertFrom-Json
    if (@($stablePreview.CreateArguments) -contains '--prerelease') { throw 'Stable preview must not contain --prerelease.' }

    Set-Content -LiteralPath (Join-Path $repo 'source.txt') -Value 'changed after tag' -NoNewline
    Invoke-Git @('commit', '-am', 'change after tag')
    Assert-Fails { & $resolver -Tag 'v0.2.1' -RepositoryDirectory $repo } 'Tag/SHA mismatch'

    Write-Output 'Release automation tests passed.'
}
finally
{
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
