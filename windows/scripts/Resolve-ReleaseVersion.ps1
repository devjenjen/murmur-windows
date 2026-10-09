[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Tag,

    [string] $RepositoryDirectory = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail
{
    param([Parameter(Mandatory)][string] $Message)
    throw "Release version resolution failed: $Message"
}

if ($Tag -match '[\r\n]' -or $Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$')
{
    Fail "Tag must match vMAJOR.MINOR.PATCH exactly: '$Tag'."
}
$version = $Matches.version

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git')))
{
    Fail "Git repository not found: '$repositoryRoot'."
}

function Invoke-GitText
{
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = @(& git -C $repositoryRoot @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        Fail "git $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return (($output -join "`n").Trim())
}

$sourceSha = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
if ($sourceSha -notmatch '^[0-9a-fA-F]{40,64}$')
{
    Fail "Checked-out source SHA is invalid: '$sourceSha'."
}

$tagSha = Invoke-GitText -Arguments @('rev-parse', '--verify', "$Tag^{commit}")
if ($tagSha -notmatch '^[0-9a-fA-F]{40,64}$')
{
    Fail "Tag '$Tag' did not resolve to a commit."
}

if (-not [string]::Equals($tagSha, $sourceSha, [StringComparison]::OrdinalIgnoreCase))
{
    Fail "Checked-out SHA '$sourceSha' does not match tag '$Tag' SHA '$tagSha'."
}

[pscustomobject]@{
    Tag = $Tag
    Version = $version
    SourceSha = $sourceSha.ToLowerInvariant()
}
