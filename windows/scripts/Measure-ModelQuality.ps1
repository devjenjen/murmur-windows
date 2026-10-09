[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $CorpusDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ModelsDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [string] $DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail
{
    param([Parameter(Mandatory)][string] $Message)
    throw "Model quality measurement failed: $Message"
}

function Resolve-Directory
{
    param([Parameter(Mandatory)][string] $Path)
    return [IO.Path]::GetFullPath($Path)
}

function Assert-WithinDirectory
{
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Candidate,
        [Parameter(Mandatory)][string] $Description
    )

    $rootPath = (Resolve-Directory -Path $Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidatePath = Resolve-Directory -Path $Candidate
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $candidatePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))
    {
        Fail "$Description escapes its root: '$candidatePath'."
    }

    return $candidatePath
}

function Assert-ValidatedModel
{
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][hashtable] $Model
    )

    $directory = Join-Path $Root $Model.Id
    if (-not (Test-Path -LiteralPath $directory -PathType Container))
    {
        Fail "Validated model directory missing: '$directory'."
    }

    $integrityPath = Join-Path $directory 'integrity.json'
    if (-not (Test-Path -LiteralPath $integrityPath -PathType Leaf))
    {
        Fail "Integrity manifest missing for model '$($Model.Id)'."
    }

    try
    {
        $integrity = Get-Content -LiteralPath $integrityPath -Raw | ConvertFrom-Json
    }
    catch
    {
        Fail "Integrity manifest is invalid for model '$($Model.Id)'."
    }

    if ($integrity.version -ne 1 -or $integrity.modelId -ne $Model.Id)
    {
        Fail "Integrity manifest identity mismatch for model '$($Model.Id)'."
    }

    $entries = @($integrity.files)
    if ($entries.Count -ne $Model.RequiredFiles.Count)
    {
        Fail "Integrity manifest file count mismatch for model '$($Model.Id)'."
    }

    foreach ($requiredFile in $Model.RequiredFiles)
    {
        $entry = @($entries | Where-Object { $_.path -eq $requiredFile })
        if ($entry.Count -ne 1)
        {
            Fail "Integrity manifest misses '$requiredFile' for model '$($Model.Id)'."
        }

        $filePath = Join-Path $directory $requiredFile
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf))
        {
            Fail "Required model file missing: '$filePath'."
        }

        $file = Get-Item -LiteralPath $filePath
        $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($file.Length -ne [int64]$entry[0].length -or $hash -ne ([string]$entry[0].sha256).ToLowerInvariant())
        {
            Fail "Integrity mismatch for '$filePath'."
        }
    }
}

$corpusRoot = Resolve-Directory -Path $CorpusDirectory
$modelsRoot = Resolve-Directory -Path $ModelsDirectory
$outputRoot = Resolve-Directory -Path $OutputDirectory
$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testProject = Join-Path $windowsRoot 'tests/Murmur.Speech.Tests/Murmur.Speech.Tests.csproj'
if (-not (Test-Path -LiteralPath $corpusRoot -PathType Container)) { Fail "Corpus directory not found: '$corpusRoot'." }
if (-not (Test-Path -LiteralPath $modelsRoot -PathType Container)) { Fail "Models directory not found: '$modelsRoot'." }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$manifestPath = Join-Path $corpusRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { Fail "Corpus manifest missing: '$manifestPath'." }
try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
catch { Fail "Corpus manifest is invalid JSON: '$manifestPath'." }
$cases = @($manifest.cases)
if ($cases.Count -eq 0) { Fail 'Corpus contains zero cases.' }
$languageCounts = @{}
foreach ($case in $cases)
{
    if ([string]::IsNullOrWhiteSpace([string]$case.id) -or
        [string]::IsNullOrWhiteSpace([string]$case.audio) -or
        [string]::IsNullOrWhiteSpace([string]$case.language) -or
        [string]::IsNullOrWhiteSpace([string]$case.reference))
    {
        Fail 'Every corpus case needs id, audio, language, and reference.'
    }

    $language = ([string]$case.language).Trim().ToLowerInvariant()
    if ($language -notin @('de', 'en'))
    {
        Fail "Corpus case '$($case.id)' has unsupported language '$($case.language)'; expected de or en."
    }
    $languageCounts[$language] = 1 + [int]($languageCounts[$language] ?? 0)

    $audioPath = Assert-WithinDirectory -Root $corpusRoot -Candidate (Join-Path $corpusRoot ([string]$case.audio)) -Description "Audio for case '$($case.id)'"
    if (-not (Test-Path -LiteralPath $audioPath -PathType Leaf)) { Fail "Audio file missing: '$audioPath'." }
}
if ($cases.Count -lt 30 -or [int]($languageCounts['de'] ?? 0) -lt 15 -or [int]($languageCounts['en'] ?? 0) -lt 15)
{
    Fail "Corpus requires at least 30 cases with at least 15 de and 15 en cases; found total=$($cases.Count), de=$([int]($languageCounts['de'] ?? 0)), en=$([int]($languageCounts['en'] ?? 0))."
}

$requiredFiles = @('encoder.int8.onnx', 'decoder.int8.onnx', 'joiner.int8.onnx', 'tokens.txt')
$models = @(
    @{ Id = 'parakeet-v3'; RequiredFiles = $requiredFiles },
    @{ Id = 'nemotron-3.5-streaming-160ms'; RequiredFiles = $requiredFiles }
)
foreach ($model in $models) { Assert-ValidatedModel -Root $modelsRoot -Model $model }

$qualityResultsDirectory = Join-Path $outputRoot 'test-results'
New-Item -ItemType Directory -Path $qualityResultsDirectory -Force | Out-Null
$previousCorpus = $env:MURMUR_QUALITY_CORPUS
$previousModels = $env:MURMUR_TEST_MODELS_ROOT
$previousOutput = $env:MURMUR_QUALITY_OUTPUT_DIRECTORY
$measurementStartedUtc = [DateTime]::UtcNow
try
{
    $env:MURMUR_QUALITY_CORPUS = $corpusRoot
    $env:MURMUR_TEST_MODELS_ROOT = $modelsRoot
    $env:MURMUR_QUALITY_OUTPUT_DIRECTORY = $outputRoot
    $testArguments = @(
        'test', $testProject,
        '--no-build', '-c', 'Release',
        '--filter', 'Category=QualityCorpus',
        '--logger', 'trx;LogFilePrefix=quality',
        '--results-directory', $qualityResultsDirectory
    )
    & $DotnetPath @testArguments
    if ($LASTEXITCODE -ne 0) { Fail "Quality test project failed with exit code $LASTEXITCODE." }
}
finally
{
    if ($null -eq $previousCorpus) { Remove-Item Env:MURMUR_QUALITY_CORPUS -ErrorAction SilentlyContinue } else { $env:MURMUR_QUALITY_CORPUS = $previousCorpus }
    if ($null -eq $previousModels) { Remove-Item Env:MURMUR_TEST_MODELS_ROOT -ErrorAction SilentlyContinue } else { $env:MURMUR_TEST_MODELS_ROOT = $previousModels }
    if ($null -eq $previousOutput) { Remove-Item Env:MURMUR_QUALITY_OUTPUT_DIRECTORY -ErrorAction SilentlyContinue } else { $env:MURMUR_QUALITY_OUTPUT_DIRECTORY = $previousOutput }
}

$reports = @(Get-ChildItem -LiteralPath $outputRoot -Filter '*.json' -File |
    Where-Object { $_.Name -ne 'quality-run.json' -and $_.LastWriteTimeUtc -ge $measurementStartedUtc.AddSeconds(-2) })
if ($reports.Count -eq 0) { Fail "Quality test produced no JSON report in '$outputRoot'." }
$reportFile = $reports | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
$report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
if (@($report.models).Count -ne 2) { Fail 'Quality report does not contain both models.' }
$repositoryDirectory = Split-Path -Parent $windowsRoot
$expectedCommitSha = (@(& git -C $repositoryDirectory rev-parse HEAD 2>$null) -join '').Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($expectedCommitSha)) { Fail 'Could not determine current repository commit SHA.' }
if (-not [string]::Equals([string]$report.commitSha, $expectedCommitSha, [StringComparison]::OrdinalIgnoreCase))
{
    Fail "Quality report commit SHA '$($report.commitSha)' does not match current HEAD '$expectedCommitSha'."
}
if ([string]::IsNullOrWhiteSpace([string]$report.commitSha)) { Fail 'Quality report has no commit SHA.' }
if ($null -eq $report.dirty) { Fail 'Quality report has no dirty flag.' }
foreach ($modelReport in @($report.models))
{
    if (@($modelReport.cases).Count -ne $cases.Count)
    {
        Fail "Quality report for '$($modelReport.modelId)' contains $(@($modelReport.cases).Count) cases; expected $($cases.Count)."
    }

    $reportedLanguageCounts = $modelReport.casesByLanguage
    if ([int]($reportedLanguageCounts.de ?? 0) -lt 15 -or [int]($reportedLanguageCounts.en ?? 0) -lt 15)
    {
        Fail "Quality report for '$($modelReport.modelId)' does not contain at least 15 de and 15 en cases."
    }

    foreach ($metricGroup in @('raw', 'formatted', 'corrected'))
    {
        $aggregate = $modelReport.aggregate.$metricGroup
        if ($null -eq $aggregate -or $null -eq $aggregate.wordErrorRate -or $null -eq $aggregate.characterErrorRate)
        {
            Fail "Quality report for '$($modelReport.modelId)' is missing $metricGroup WER/CER metrics."
        }
    }
}

Write-Output "Quality measurement passed. Report: $($reportFile.FullName)"
