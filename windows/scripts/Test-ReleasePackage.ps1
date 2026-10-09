[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ExpectedVersion,

    [string] $ArchivePath = '',

    [string] $InstallerPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail
{
    param([Parameter(Mandatory)][string] $Message)
    throw "Release package validation failed: $Message"
}

function Resolve-PublishFile
{
    param([Parameter(Mandatory)][string] $RelativePath)

    if ([IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '(^|[\\/])\.\.([\\/]|$)')
    {
        Fail "Inventory path is not relative and contained: '$RelativePath'."
    }

    $candidate = [IO.Path]::GetFullPath((Join-Path $PublishDirectory $RelativePath))
    $publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $publishPrefix = $publishRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        Fail "Inventory path escapes publish directory: '$RelativePath'."
    }

    return $candidate
}

function Normalize-Version
{
    param([Parameter(Mandatory)][string] $Version)

    $clean = $Version.Trim().TrimStart('v')
    $clean = $clean.Split('+')[0]
    try
    {
        return [Version]::Parse($clean)
    }
    catch
    {
        Fail "Invalid version '$Version'."
    }
}

function Test-VersionEqual
{
    param(
        [Parameter(Mandatory)][Version] $Actual,
        [Parameter(Mandatory)][Version] $Expected
    )

    return $Actual.Major -eq $Expected.Major -and
        $Actual.Minor -eq $Expected.Minor -and
        $Actual.Build -eq $Expected.Build
}

function Get-InventoryArray
{
    param(
        [Parameter(Mandatory)][object] $Component,
        [Parameter(Mandatory)][string] $PropertyName
    )

    $property = $Component.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value)
    {
        Fail "Component '$($Component.component)' misses '$PropertyName'."
    }

    return @($property.Value)
}

$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container))
{
    Fail "Publish directory not found: '$publishRoot'."
}

$inventoryPath = Join-Path $PSScriptRoot '..\installer\third-party-components.json'
if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf))
{
    Fail "Inventory not found: '$inventoryPath'."
}

try
{
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
}
catch
{
    Fail "Inventory is not valid JSON: '$inventoryPath'."
}

$allowedGlobs = @($inventory.allowedRuntimeGlobs)
if ($allowedGlobs.Count -eq 0)
{
    Fail 'Inventory contains no allowed runtime globs.'
}

$components = @($inventory.components)
if ($components.Count -eq 0)
{
    Fail 'Inventory contains no components.'
}

$exePath = Join-Path $publishRoot 'Murmur.App.exe'
$depsPath = Join-Path $publishRoot 'Murmur.App.deps.json'
$runtimeConfigPath = Join-Path $publishRoot 'Murmur.App.runtimeconfig.json'
foreach ($required in @($exePath, $depsPath, $runtimeConfigPath))
{
    if (-not (Test-Path -LiteralPath $required -PathType Leaf))
    {
        Fail "Required application file missing: '$required'."
    }
}

$actualVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
if ([string]::IsNullOrWhiteSpace($actualVersionText))
{
    $actualVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
}
$actualVersion = Normalize-Version $actualVersionText
$expectedVersionValue = Normalize-Version $ExpectedVersion
if (-not (Test-VersionEqual $actualVersion $expectedVersionValue))
{
    Fail "EXE version '$actualVersionText' does not match expected '$ExpectedVersion'."
}

try
{
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
}
catch
{
    Fail "Dependency manifest is not valid JSON: '$depsPath'."
}

$inventoryPackageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$requiredRuntimePatterns = [Collections.Generic.List[string]]::new()
$requiredLicensePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($component in $components)
{
    foreach ($packageId in (Get-InventoryArray -Component $component -PropertyName 'packageIds'))
    {
        [void]$inventoryPackageIds.Add([string]$packageId)
    }

    foreach ($runtimePattern in (Get-InventoryArray -Component $component -PropertyName 'runtimeFiles'))
    {
        [void]$requiredRuntimePatterns.Add([string]$runtimePattern)
    }

    foreach ($licensePath in (Get-InventoryArray -Component $component -PropertyName 'licenseFiles'))
    {
        $licensePathText = [string]$licensePath
        [void]$requiredLicensePaths.Add($licensePathText)
        $licenseFile = Resolve-PublishFile $licensePathText
        if (-not (Test-Path -LiteralPath $licenseFile -PathType Leaf))
        {
            Fail "Required license/notice missing: '$licensePathText'."
        }
        if ((Get-Item -LiteralPath $licenseFile).Length -le 0)
        {
            Fail "Required license/notice is empty: '$licensePathText'."
        }
    }
}

foreach ($library in $deps.libraries.PSObject.Properties.Name)
{
    $packageId = $library.Split('/')[0]
    if ($packageId.StartsWith('Murmur.', [StringComparison]::OrdinalIgnoreCase))
    {
        continue
    }

    if (-not $inventoryPackageIds.Contains($packageId))
    {
        Fail "Published dependency '$packageId' is absent from inventory."
    }
}

$publishFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File)
foreach ($pattern in $requiredRuntimePatterns)
{
    if (-not ($publishFiles | Where-Object { $_.Name -like $pattern }))
    {
        Fail "Required runtime asset pattern has no match: '$pattern'."
    }
}

foreach ($nativeName in @(
    'av_libglesv2.dll',
    'libHarfBuzzSharp.dll',
    'libSkiaSharp.dll',
    'onnxruntime.dll',
    'sherpa-onnx-c-api.dll'
))
{
    if (-not ($publishFiles | Where-Object { $_.Name -eq $nativeName }))
    {
        Fail "Required native runtime DLL missing: '$nativeName'."
    }
}

$allowedDocumentation = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($documentation in @('LICENSE', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md'))
{
    [void]$allowedDocumentation.Add($documentation)
}
foreach ($licensePath in $requiredLicensePaths)
{
    [void]$allowedDocumentation.Add($licensePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

$forbiddenPattern = '(?i)(^|[\\/])(?:bin|obj|TestResults|\.git|\.github|\.codex|\.agents)([\\/]|$)|history\.jsonl$|settings\.json$|feedback\.wav$|\.onnx$|\.tar(?:\.bz2)?$|\.secret(?:$|\.)|(?:^|[\\/])\.env(?:$|\.)|(?:^|[\\/])models?(?:$|[\\/])'
foreach ($file in $publishFiles)
{
    $relativePath = $file.FullName.Substring($publishRoot.Length).TrimStart(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ($relativePath -match $forbiddenPattern)
    {
        Fail "Forbidden build/test/user asset in publish output: '$relativePath'."
    }

    if ($allowedDocumentation.Contains($relativePath))
    {
        continue
    }

    $runtimeMatch = $false
    foreach ($glob in $allowedGlobs)
    {
        if ($file.Name -like [string]$glob)
        {
            $runtimeMatch = $true
            break
        }
    }
    if (-not $runtimeMatch)
    {
        Fail "Undocumented publish asset: '$relativePath'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath))
{
    $installerFullPath = [IO.Path]::GetFullPath($InstallerPath)
    if (-not (Test-Path -LiteralPath $installerFullPath -PathType Leaf))
    {
        Fail "Installer not found: '$installerFullPath'."
    }

    $installerVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($installerFullPath).ProductVersion
    if ([string]::IsNullOrWhiteSpace($installerVersionText))
    {
        $installerVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($installerFullPath).FileVersion
    }

    $installerVersion = Normalize-Version $installerVersionText
    if (-not (Test-VersionEqual -Actual $installerVersion -Expected $expectedVersionValue))
    {
        Fail "Installer version '$installerVersionText' does not match expected '$ExpectedVersion'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($ArchivePath))
{
    $archiveFullPath = [IO.Path]::GetFullPath($ArchivePath)
    if (-not (Test-Path -LiteralPath $archiveFullPath -PathType Leaf))
    {
        Fail "Portable archive not found: '$archiveFullPath'."
    }

    $archiveExtractRoot = Join-Path ([IO.Path]::GetTempPath()) ('murmur-package-archive-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $archiveExtractRoot -Force | Out-Null
    try
    {
        Expand-Archive -LiteralPath $archiveFullPath -DestinationPath $archiveExtractRoot -Force
        try
        {
            & $PSCommandPath -PublishDirectory $archiveExtractRoot -ExpectedVersion $ExpectedVersion
        }
        catch
        {
            Fail "Portable archive contents failed validation: $($_.Exception.Message)"
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $archiveExtractRoot)
        {
            Remove-Item -LiteralPath $archiveExtractRoot -Recurse -Force
        }
    }
}

Write-Host "Release package valid: $publishRoot (version $actualVersionText)"
