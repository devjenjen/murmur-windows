[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release',

    [switch] $IncludeRealModels,

    [string] $OutputDirectory = '',

    [string] $Tag = '',

    [string] $ReportPath = '',

    # Test seam. Production callers keep the default `dotnet` command.
    [string] $DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Checks = [ordered]@{
    Restore = 'NOT RUN'
    Build = 'NOT RUN'
    UnitTests = 'NOT RUN'
    PackageAudit = 'NOT RUN'
    RealModelTests = 'NOT RUN'
}
$script:Commands = [System.Collections.Generic.List[string]]::new()
$script:TestSummaries = [System.Collections.Generic.List[object]]::new()
$script:FailureMessage = ''
$script:RunStarted = Get-Date
$script:ReleaseTag = ''
$script:ReleaseVersion = ''

$scriptsDirectory = $PSScriptRoot
$windowsDirectory = Split-Path -Parent $scriptsDirectory
$repositoryDirectory = Split-Path -Parent $windowsDirectory
$reportPath = if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    Join-Path $windowsDirectory 'artifacts/verification/verification-report.md'
} elseif ([IO.Path]::IsPathRooted($ReportPath)) {
    [IO.Path]::GetFullPath($ReportPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryDirectory $ReportPath))
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = 'artifacts/verification'
}

if ([IO.Path]::IsPathRooted($OutputDirectory))
{
    $artifactDirectory = [IO.Path]::GetFullPath($OutputDirectory)
}
else
{
    $artifactDirectory = [IO.Path]::GetFullPath((Join-Path $windowsDirectory $OutputDirectory))
}

$unitResultsDirectory = Join-Path $artifactDirectory 'unit'
$realModelResultsDirectory = Join-Path $artifactDirectory 'real-model'
$buildBinlogPath = Join-Path $artifactDirectory 'build.binlog'
$packageAuditPath = Join-Path $artifactDirectory 'package-audit.json'
$packageAuditErrorPath = Join-Path $artifactDirectory 'package-audit.stderr.log'
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssfff', [Globalization.CultureInfo]::InvariantCulture)

function Set-CheckStatus
{
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Status
    )

    $script:Checks[$Name] = $Status
}

function Convert-ToCommandText
{
    param([Parameter(Mandatory)][string[]] $Arguments)

    $escaped = foreach ($argument in $Arguments)
    {
        if ($argument -match '[\s;&|<>]')
        {
            "'$($argument.Replace("'", "''"))'"
        }
        else
        {
            $argument
        }
    }

    return "dotnet $($escaped -join ' ')"
}

function Invoke-DotnetCommand
{
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage,
        [Parameter(Mandatory)][string] $StdoutPath,
        [Parameter(Mandatory)][string] $StderrPath
    )

    $script:Commands.Add((Convert-ToCommandText -Arguments $Arguments))
    & $DotnetPath @Arguments 1> $StdoutPath 2> $StderrPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        throw "$FailureMessage Exit code: $exitCode. See '$StdoutPath' and '$StderrPath'."
    }
}

function Invoke-GitText
{
    param([Parameter(Mandatory)][string[]] $Arguments)

    $result = @(& git @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode. $($result -join ' ')"
    }

    return $result
}

function Get-TrxSummary
{
    param(
        [Parameter(Mandatory)][string] $ResultsDirectory,
        [Parameter(Mandatory)][string] $FilePrefix
    )

    $files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter "$FilePrefix*.trx" -File -ErrorAction SilentlyContinue)
    $summary = [ordered]@{
        Files = $files.Count
        Total = 0
        Passed = 0
        Failed = 0
        Skipped = 0
        NotExecuted = 0
    }

    foreach ($file in $files)
    {
        [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ($null -eq $counters)
        {
            throw "TRX '$($file.FullName)' has no result counters."
        }

        foreach ($name in @('Total', 'Passed', 'Failed', 'NotExecuted'))
        {
            if ($null -ne $counters.$name)
            {
                $summary[$name] += [int]$counters.$name
            }
        }
    }

    $summary.Skipped = $summary.NotExecuted
    return [pscustomobject]$summary
}

function Assert-TestSummary
{
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][object] $Summary
    )

    if ($Summary.Files -eq 0 -or $Summary.Total -eq 0)
    {
        throw "$Name produced no executed tests."
    }

    if ($Summary.Failed -ne 0 -or $Summary.NotExecuted -ne 0 -or $Summary.Passed -ne $Summary.Total)
    {
        throw "$Name did not execute a fully passing test set: $($Summary.Passed)/$($Summary.Total) passed, $($Summary.Failed) failed, $($Summary.NotExecuted) not executed."
    }
}

function Find-VulnerabilityEntries
{
    param(
        [AllowNull()][object] $Value,
        [string] $Path = 'root'
    )

    if ($null -eq $Value)
    {
        return
    }

    if ($Value -is [System.Collections.IEnumerable] -and
        $Value -isnot [string] -and
        $Value -isnot [System.Collections.IDictionary])
    {
        $index = 0
        foreach ($item in $Value)
        {
            Find-VulnerabilityEntries -Value $item -Path "$Path[$index]"
            $index++
        }

        return
    }

    foreach ($property in $Value.PSObject.Properties)
    {
        $propertyPath = "$Path.$($property.Name)"
        if ($property.Name -ieq 'vulnerabilities')
        {
            foreach ($finding in @($property.Value))
            {
                if ($null -ne $finding)
                {
                    [pscustomobject]@{
                        Path = $propertyPath
                        Finding = ($finding | ConvertTo-Json -Compress -Depth 10)
                    }
                }
            }
        }

        Find-VulnerabilityEntries -Value $property.Value -Path $propertyPath
    }
}

function Get-SafeInlineText
{
    param([AllowNull()][object] $Value)

    if ($null -eq $Value)
    {
        return ''
    }

    $text = [string]$Value
    return $text.Replace('`', '\`').Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Write-VerificationReport
{
    param(
        [Parameter(Mandatory)][ValidateSet('PASS', 'FAIL', 'NOT RUN')][string] $Status
    )

    $sourceSha = $script:SourceSha
    $branch = $script:Branch
    $statusText = if (@($script:GitStatus).Count -eq 0) { '(clean)' } else { $script:GitStatus -join '; ' }
    $localTagsText = if (@($script:LocalTags).Count -eq 0) { '(none)' } else { $script:LocalTags -join ', ' }
    $remoteTagsText = if (@($script:RemoteTags).Count -eq 0) { '(none or unavailable)' } else { $script:RemoteTags -join '; ' }
    $failureText = if ([string]::IsNullOrWhiteSpace($script:FailureMessage)) { 'None' } else { $script:FailureMessage }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Release verification report')
    $lines.Add('')
    $lines.Add("- Status: **$Status**")
    $lines.Add("- Started: $($script:RunStarted.ToString('o'))")
    $lines.Add("- Finished: $((Get-Date).ToString('o'))")
    $lines.Add(('- Configuration: `{0}`' -f $Configuration))
    if (-not [string]::IsNullOrWhiteSpace($script:ReleaseTag))
    {
        $lines.Add(('- Release tag: `{0}`' -f $script:ReleaseTag))
        $lines.Add(('- Release version: `{0}`' -f $script:ReleaseVersion))
    }
    $lines.Add(('- Verified software commit: `{0}`' -f $sourceSha))
    $lines.Add(('- Branch: `{0}`' -f $branch))
    $lines.Add(('- Git status at report time: `{0}`' -f $statusText))
    $lines.Add(('- Local tags: `{0}`' -f $localTagsText))
    $lines.Add(('- Remote tags: `{0}`' -f $remoteTagsText))
    $lines.Add(('- Artifact directory: `{0}`' -f $artifactDirectory))
    $lines.Add("- Failure: $(Get-SafeInlineText -Value $failureText)")
    $lines.Add('')
    $lines.Add('## Checks')
    $lines.Add('')
    $lines.Add('| Check | Result |')
    $lines.Add('| --- | --- |')
    foreach ($entry in $script:Checks.GetEnumerator())
    {
        $lines.Add("| $($entry.Key) | $($entry.Value) |")
    }

    $lines.Add('')
    $lines.Add('## Test counts')
    $lines.Add('')
    if (@($script:TestSummaries).Count -eq 0)
    {
        $lines.Add('NOT RUN')
    }
    else
    {
        $lines.Add('| Suite | Files | Total | Passed | Failed | Skipped |')
        $lines.Add('| --- | ---: | ---: | ---: | ---: | ---: |')
        foreach ($summary in $script:TestSummaries)
        {
            $lines.Add("| $($summary.Name) | $($summary.Result.Files) | $($summary.Result.Total) | $($summary.Result.Passed) | $($summary.Result.Failed) | $($summary.Result.Skipped) |")
        }
    }

    $lines.Add('')
    $lines.Add('## Commands')
    $lines.Add('')
    if (@($script:Commands).Count -eq 0)
    {
        $lines.Add('NOT RUN')
    }
    else
    {
        foreach ($command in $script:Commands)
        {
            $lines.Add(('- `{0}`' -f $command))
        }
    }

    $lines.Add('')
    $lines.Add('## Open manual checks')
    $lines.Add('')
    $lines.Add('- Real-model inference, installer lifecycle, GUI, audio-device, DPI, and VM checks remain **NOT RUN** unless explicitly included below.')
    $lines.Add('- Unsigned Windows binaries require SmartScreen wording and owner acceptance before publication.')

    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
    Set-Content -LiteralPath $reportPath -Value $lines -Encoding utf8NoBOM
}

try
{
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $unitResultsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $realModelResultsDirectory -Force | Out-Null

    Set-Location -LiteralPath $windowsDirectory

    $script:SourceSha = (Invoke-GitText -Arguments @('rev-parse', 'HEAD') | Select-Object -First 1).ToString().Trim()
    $script:Branch = (Invoke-GitText -Arguments @('branch', '--show-current') | Select-Object -First 1).ToString().Trim()
    $script:GitStatus = @(Invoke-GitText -Arguments @('status', '--short', '--branch'))
    $script:LocalTags = @(Invoke-GitText -Arguments @('tag', '--sort=-version:refname'))
    $remoteName = @(Invoke-GitText -Arguments @('remote')) | Where-Object { $_ -eq 'origin' } | Select-Object -First 1
    if ($null -eq $remoteName)
    {
        $script:RemoteTags = @()
    }
    else
    {
        try { $script:RemoteTags = @(Invoke-GitText -Arguments @('ls-remote', '--tags', '--refs', 'origin')) }
        catch { $script:RemoteTags = @('(unavailable)') }
    }

    if (-not [string]::IsNullOrWhiteSpace($Tag))
    {
        $resolver = Join-Path $scriptsDirectory 'Resolve-ReleaseVersion.ps1'
        $resolved = & $resolver -Tag $Tag -RepositoryDirectory $repositoryDirectory
        $script:ReleaseTag = [string]$resolved.Tag
        $script:ReleaseVersion = [string]$resolved.Version
        if (-not [string]::Equals($script:SourceSha, [string]$resolved.SourceSha, [StringComparison]::OrdinalIgnoreCase))
        {
            throw 'Resolved release SHA does not match verification SHA.'
        }
    }

    Invoke-DotnetCommand -Arguments @('restore', 'Murmur.sln', '--runtime', 'win-x64') `
        -FailureMessage 'Restore failed.' `
        -StdoutPath (Join-Path $artifactDirectory 'restore.log') `
        -StderrPath (Join-Path $artifactDirectory 'restore.stderr.log')
    Set-CheckStatus -Name 'Restore' -Status 'PASS'

    Invoke-DotnetCommand -Arguments @('build', 'Murmur.sln', '--no-restore', '-c', $Configuration, '--no-incremental', '-warnaserror', "-bl:$buildBinlogPath") `
        -FailureMessage 'Build failed.' `
        -StdoutPath (Join-Path $artifactDirectory 'build.log') `
        -StderrPath (Join-Path $artifactDirectory 'build.stderr.log')
    Set-CheckStatus -Name 'Build' -Status 'PASS'

    $unitPrefix = "unit-$runId"
    Invoke-DotnetCommand -Arguments @(
        'test', 'Murmur.sln', '--no-build', '-c', $Configuration,
        '--filter', 'Category!=RealModel&Category!=QualityCorpus',
        '--logger', "trx;LogFilePrefix=$unitPrefix",
        '--results-directory', $unitResultsDirectory
    ) `
        -FailureMessage 'Unit tests failed.' `
        -StdoutPath (Join-Path $artifactDirectory 'unit.log') `
        -StderrPath (Join-Path $artifactDirectory 'unit.stderr.log')

    $unitSummary = Get-TrxSummary -ResultsDirectory $unitResultsDirectory -FilePrefix $unitPrefix
    Assert-TestSummary -Name 'Unit tests' -Summary $unitSummary
    $script:TestSummaries.Add([pscustomobject]@{ Name = 'Unit / headless'; Result = $unitSummary })
    Set-CheckStatus -Name 'UnitTests' -Status "PASS ($($unitSummary.Passed)/$($unitSummary.Total) passed)"

    Invoke-DotnetCommand -Arguments @(
        'package', 'list', '--project', 'Murmur.sln', '--no-restore', '--vulnerable', '--include-transitive', '--format', 'json'
    ) `
        -FailureMessage 'Package audit command failed.' `
        -StdoutPath $packageAuditPath `
        -StderrPath $packageAuditErrorPath

    $auditJson = Get-Content -LiteralPath $packageAuditPath -Raw
    if ([string]::IsNullOrWhiteSpace($auditJson))
    {
        throw "Package audit returned empty JSON: '$packageAuditPath'."
    }

    try
    {
        $audit = $auditJson | ConvertFrom-Json -Depth 100
    }
    catch
    {
        throw "Package audit JSON is invalid: '$packageAuditPath'. $($_.Exception.Message)"
    }

    $findings = @(Find-VulnerabilityEntries -Value $audit)
    if ($findings.Count -gt 0)
    {
        $details = ($findings | ForEach-Object { "$($_.Path): $($_.Finding)" }) -join '; '
        throw "Package audit found $($findings.Count) vulnerability entries. $details"
    }
    Set-CheckStatus -Name 'PackageAudit' -Status 'PASS (no vulnerability entries)'

    if ($IncludeRealModels)
    {
        $previousModelRoot = $env:MURMUR_TEST_MODELS_ROOT
        try
        {
            if ([string]::IsNullOrWhiteSpace($env:MURMUR_TEST_MODELS_ROOT))
            {
                $env:MURMUR_TEST_MODELS_ROOT = Join-Path $env:TEMP 'Murmur-release-tests'
            }

            $realModelPrefix = "real-model-$runId"
            Invoke-DotnetCommand -Arguments @(
                'test', 'tests/Murmur.Speech.Tests/Murmur.Speech.Tests.csproj', '--no-build', '-c', $Configuration,
                '--filter', 'Category=RealModel',
                '--logger', "trx;LogFilePrefix=$realModelPrefix",
                '--results-directory', $realModelResultsDirectory
            ) `
                -FailureMessage 'Real-model tests failed.' `
                -StdoutPath (Join-Path $artifactDirectory 'real-model.log') `
                -StderrPath (Join-Path $artifactDirectory 'real-model.stderr.log')

            $realModelSummary = Get-TrxSummary -ResultsDirectory $realModelResultsDirectory -FilePrefix $realModelPrefix
            Assert-TestSummary -Name 'Real-model tests' -Summary $realModelSummary
            $script:TestSummaries.Add([pscustomobject]@{ Name = 'Real model'; Result = $realModelSummary })
            Set-CheckStatus -Name 'RealModelTests' -Status "PASS ($($realModelSummary.Passed)/$($realModelSummary.Total) passed)"
        }
        finally
        {
            $env:MURMUR_TEST_MODELS_ROOT = $previousModelRoot
        }
    }

    Write-VerificationReport -Status 'PASS'
    Write-Output "Release verification passed. Report: $reportPath"
}
catch
{
    $script:FailureMessage = $_.Exception.Message
    try
    {
        Write-VerificationReport -Status 'FAIL'
    }
    catch
    {
        Write-Error "Could not write verification report: $($_.Exception.Message)"
    }

    Write-Error $script:FailureMessage
    exit 1
}
