[CmdletBinding()]
param(
    [string] $PublishDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = $PSScriptRoot
$repositoryDirectory = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
$validator = Join-Path $scriptDirectory 'Test-ReleasePackage.ps1'
$sourcePublish = if (-not [string]::IsNullOrWhiteSpace($PublishDirectory))
{
    [IO.Path]::GetFullPath($PublishDirectory)
}
else
{
    @(
        (Join-Path $repositoryDirectory 'windows\artifacts\final-release\publish\win-x64'),
        (Join-Path $repositoryDirectory 'windows\artifacts\release\publish\win-x64')
    ) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        Select-Object -First 1
}
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Murmur-package-validator-' + [Guid]::NewGuid().ToString('N'))
$fixturePublish = Join-Path $fixtureRoot 'publish'

function Invoke-Validator
{
    param(
        [Parameter(Mandatory)][string] $PublishDirectory,
        [string] $ArchivePath = '',
        [string] $InstallerPath = ''
    )

    $arguments = @('-NoProfile', '-File', $validator, '-PublishDirectory', $PublishDirectory, '-ExpectedVersion', '0.2.0')
    if (-not [string]::IsNullOrWhiteSpace($ArchivePath)) { $arguments += @('-ArchivePath', $ArchivePath) }
    if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) { $arguments += @('-InstallerPath', $InstallerPath) }
    & pwsh @arguments *> $null
    return $LASTEXITCODE
}

function Assert-ExitCode
{
    param(
        [Parameter(Mandatory)][int] $Actual,
        [Parameter(Mandatory)][bool] $ShouldPass,
        [Parameter(Mandatory)][string] $Scenario
    )

    if ($ShouldPass -and $Actual -ne 0)
    {
        throw "$Scenario expected success, got exit $Actual."
    }
    if (-not $ShouldPass -and $Actual -eq 0)
    {
        throw "$Scenario expected failure, got success."
    }
}

try
{
    if (-not (Test-Path -LiteralPath $sourcePublish -PathType Container))
    {
        throw "Build a release publish or pass -PublishDirectory before running package-validator tests: '$sourcePublish'."
    }

    New-Item -ItemType Directory -Path $fixturePublish -Force | Out-Null
    Copy-Item -Path (Join-Path $sourcePublish '*') -Destination $fixturePublish -Recurse -Force

    Assert-ExitCode (Invoke-Validator -PublishDirectory $fixturePublish) $true 'Complete publish'

    $fixtureArchive = Join-Path $fixtureRoot 'Murmur-0.2.0-win-x64.zip'
    Compress-Archive -Path (Join-Path $fixturePublish '*') -DestinationPath $fixtureArchive
    Assert-ExitCode (Invoke-Validator -PublishDirectory $fixturePublish -ArchivePath $fixtureArchive) $true 'Archive contents'

    Remove-Item -LiteralPath (Join-Path $fixturePublish 'licenses\Avalonia.MIT.txt') -Force
    Assert-ExitCode (Invoke-Validator -PublishDirectory $fixturePublish) $false 'Missing license'

    Copy-Item -LiteralPath (Join-Path $sourcePublish 'licenses\Avalonia.MIT.txt') -Destination (Join-Path $fixturePublish 'licenses\Avalonia.MIT.txt') -Force
    Set-Content -LiteralPath (Join-Path $fixturePublish 'history.jsonl') -Value '{}' -NoNewline
    Assert-ExitCode (Invoke-Validator -PublishDirectory $fixturePublish) $false 'History data'
    Remove-Item -LiteralPath (Join-Path $fixturePublish 'history.jsonl') -Force

    New-Item -ItemType Directory -Path (Join-Path $fixturePublish 'Sounds') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixturePublish 'Sounds\feedback.wav') -Value 'synthetic' -NoNewline
    Assert-ExitCode (Invoke-Validator -PublishDirectory $fixturePublish) $false 'Feedback sound'

    Write-Host 'Release package validator tests passed.'
}
finally
{
    if (Test-Path -LiteralPath $fixtureRoot)
    {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
