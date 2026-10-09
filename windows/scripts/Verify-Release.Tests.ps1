$ErrorActionPreference = 'Stop'

$testRoot = Join-Path $env:TEMP "murmur-verify-release-$([Guid]::NewGuid().ToString('N'))"
$fakeDotnet = Join-Path $testRoot 'fake-dotnet.ps1'
$outputDirectory = Join-Path $testRoot 'verification'

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try
{
    @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
$logPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'calls.log'
Add-Content -LiteralPath $logPath -Value ($Arguments -join ' ')
exit 17
'@ | Set-Content -LiteralPath $fakeDotnet -Encoding utf8

    $scriptPath = Join-Path $PSScriptRoot 'Verify-Release.ps1'
    if (-not (Test-Path -LiteralPath $scriptPath))
    {
        throw "Verify-Release.ps1 is missing."
    }

    $output = & pwsh -NoProfile -NonInteractive -File $scriptPath `
        -DotnetPath $fakeDotnet `
        -OutputDirectory $outputDirectory `
        -ReportPath (Join-Path $testRoot 'fail-fast-report.md') 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0)
    {
        throw "Expected Verify-Release.ps1 to fail on the first native error. Output: $output"
    }

    $callsPath = Join-Path $testRoot 'calls.log'
    if (-not (Test-Path -LiteralPath $callsPath))
    {
        throw "Expected fake dotnet to be invoked. Output: $($output -join ' | ')"
    }

    $calls = @(Get-Content -LiteralPath $callsPath)
    if ($calls.Count -ne 1 -or $calls[0] -notmatch '^restore\s')
    {
        throw "Expected exactly one restore invocation before failure, got: $($calls -join ' | ')"
    }
}
finally
{
    if (Test-Path -LiteralPath $testRoot)
    {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

$zeroTestRoot = Join-Path $env:TEMP "murmur-verify-zero-tests-$([Guid]::NewGuid().ToString('N'))"
$zeroFakeDotnet = Join-Path $zeroTestRoot 'fake-dotnet.ps1'
$zeroOutputDirectory = Join-Path $zeroTestRoot 'verification'
New-Item -ItemType Directory -Path $zeroTestRoot -Force | Out-Null

try
{
    @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
if ($Arguments[0] -eq 'package') { '{}' }
exit 0
'@ | Set-Content -LiteralPath $zeroFakeDotnet -Encoding utf8

    $zeroOutput = & pwsh -NoProfile -NonInteractive -File $scriptPath `
        -DotnetPath $zeroFakeDotnet `
        -OutputDirectory $zeroOutputDirectory `
        -ReportPath (Join-Path $zeroTestRoot 'zero-tests-report.md') 2>&1
    $zeroExitCode = $LASTEXITCODE
    if ($zeroExitCode -eq 0 -or ($zeroOutput -join ' ') -notmatch 'no executed tests')
    {
        throw "Expected a zero-test verification run to fail. Exit: $zeroExitCode. Output: $($zeroOutput -join ' | ')"
    }
}
finally
{
    if (Test-Path -LiteralPath $zeroTestRoot)
    {
        Remove-Item -LiteralPath $zeroTestRoot -Recurse -Force
    }
}

Write-Output 'Verify-Release fail-fast test passed.'
