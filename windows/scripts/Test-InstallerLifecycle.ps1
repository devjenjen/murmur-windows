# Murmur installer lifecycle smoke test.
# Requires a disposable Windows VM/account. Never run against a production profile.
[CmdletBinding()]
param(
    [switch] $DisposableEnvironment,
    [string] $InstallerPath = '',
    [string] $TestRoot = '',
    [string] $UserDataRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $DisposableEnvironment)
{
    throw 'Refusing installer lifecycle test. Re-run with -DisposableEnvironment in an isolated VM/account.'
}

if ([string]::IsNullOrWhiteSpace($TestRoot))
{
    throw 'TestRoot is required. Use a new directory under %TEMP% named Murmur-installer-lifecycle-*.'
}

if ([string]::IsNullOrWhiteSpace($UserDataRoot))
{
    throw 'UserDataRoot is required. Pass the disposable account path %LOCALAPPDATA%\Murmur.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$resolvedTestRoot = [IO.Path]::GetFullPath($TestRoot)
$testRootPrefix = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedTestRoot.StartsWith($testRootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($resolvedTestRoot) -notlike 'Murmur-installer-lifecycle-*')
{
    throw "Unsafe TestRoot '$resolvedTestRoot'. Must be a named child of '$tempRoot'."
}

if (Test-Path -LiteralPath $resolvedTestRoot)
{
    $rootItem = Get-Item -LiteralPath $resolvedTestRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
    {
        throw "Refusing reparse-point TestRoot '$resolvedTestRoot'."
    }

    if (@(Get-ChildItem -LiteralPath $resolvedTestRoot -Force).Count -ne 0)
    {
        throw "TestRoot must be new or empty: '$resolvedTestRoot'."
    }
}
else
{
    New-Item -ItemType Directory -Path $resolvedTestRoot -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($InstallerPath))
{
    $installerDirectory = Join-Path $PSScriptRoot '..\artifacts\release\installer'
    $candidate = Get-ChildItem -LiteralPath $installerDirectory -Filter 'Murmur-Setup-v*.exe' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate)
    {
        throw "No installer found in '$installerDirectory'."
    }
    $InstallerPath = $candidate.FullName
}

$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf))
{
    throw "Installer not found: '$InstallerPath'."
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'Murmur'
$installPath = Join-Path $resolvedTestRoot 'install'
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$expectedDataPath = [IO.Path]::GetFullPath((Join-Path $localAppData 'Murmur'))
$dataPath = [IO.Path]::GetFullPath($UserDataRoot)
if (-not [string]::Equals($dataPath, $expectedDataPath, [StringComparison]::OrdinalIgnoreCase))
{
    throw "UserDataRoot must be the disposable account's actual '%LOCALAPPDATA%\Murmur' path: '$expectedDataPath'."
}

$hadDataDirectory = Test-Path -LiteralPath $dataPath -PathType Container
$settingsSentinel = Join-Path $dataPath 'settings.json'
$hadOriginalSentinel = Test-Path -LiteralPath $settingsSentinel -PathType Leaf
$originalSentinelBytes = if ($hadOriginalSentinel) { [IO.File]::ReadAllBytes($settingsSentinel) } else { $null }
$foreignPath = Join-Path $resolvedTestRoot 'foreign\Murmur.App.exe'
$foreignCommand = '"{0}"' -f $foreignPath
$hadOriginalRunValue = $false
$originalRunValue = $null

function Get-MurmurRunValue
{
    if (-not (Test-Path -LiteralPath $runKey))
    {
        return $null
    }

    $property = Get-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
    if ($null -eq $property)
    {
        return $null
    }

    return [string]$property.$runValueName
}

function Assert-Equal
{
    param(
        [AllowNull()][string] $Actual,
        [AllowNull()][string] $Expected,
        [Parameter(Mandatory)][string] $Message
    )

    if ($Actual -cne $Expected)
    {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Invoke-Installer
{
    $quotedInstallPath = '/DIR="{0}"' -f $installPath
    $process = Start-Process -FilePath $InstallerPath -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        $quotedInstallPath
    ) -Wait -PassThru
    if ($process.ExitCode -ne 0)
    {
        throw "Installer failed with exit code $($process.ExitCode)."
    }
}

function Invoke-Uninstaller
{
    $uninstaller = Join-Path $installPath 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf))
    {
        throw "Uninstaller not found: '$uninstaller'."
    }

    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    if ($process.ExitCode -ne 0)
    {
        throw "Uninstaller failed with exit code $($process.ExitCode)."
    }
}

try
{
    $existingRunValue = Get-MurmurRunValue
    if ($null -ne $existingRunValue)
    {
        $hadOriginalRunValue = $true
        $originalRunValue = $existingRunValue
    }

    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    Set-Content -LiteralPath $settingsSentinel -Value 'preserve-me' -NoNewline

    Write-Host 'Install...'
    Invoke-Installer
    $installedExe = Join-Path $installPath 'Murmur.App.exe'
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf))
    {
        throw "Installed executable not found: '$installedExe'."
    }

    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKey -Name $runValueName -Value ('"{0}"' -f $installedExe)
    Assert-Equal (Get-MurmurRunValue) ('"{0}"' -f $installedExe) 'Install startup value mismatch.'

    Write-Host 'Upgrade...'
    Invoke-Installer
    Assert-Equal (Get-MurmurRunValue) ('"{0}"' -f $installedExe) 'Upgrade changed startup value.'
    Assert-Equal (Get-Content -LiteralPath $settingsSentinel -Raw) 'preserve-me' 'Upgrade changed external data.'

    Write-Host 'Uninstall own startup value...'
    Invoke-Uninstaller
    if ($null -ne (Get-MurmurRunValue))
    {
        throw 'Installer-owned startup value was not removed.'
    }
    Assert-Equal (Get-Content -LiteralPath $settingsSentinel -Raw) 'preserve-me' 'Uninstall removed external data.'

    Write-Host 'Install and uninstall foreign startup value...'
    Invoke-Installer
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKey -Name $runValueName -Value $foreignCommand
    Invoke-Uninstaller
    Assert-Equal (Get-MurmurRunValue) $foreignCommand 'Uninstall removed foreign startup value.'

    Write-Host 'Installer lifecycle smoke passed.'
}
finally
{
    if ($hadOriginalRunValue)
    {
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $runKey -Name $runValueName -Value $originalRunValue
    }
    elseif (Test-Path -LiteralPath $runKey)
    {
        Remove-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }

    if ($hadOriginalSentinel)
    {
        [IO.File]::WriteAllBytes($settingsSentinel, $originalSentinelBytes)
    }
    elseif (Test-Path -LiteralPath $settingsSentinel -PathType Leaf)
    {
        Remove-Item -LiteralPath $settingsSentinel -Force
    }

    if (-not $hadDataDirectory -and (Test-Path -LiteralPath $dataPath -PathType Container) -and
        @(Get-ChildItem -LiteralPath $dataPath -Force).Count -eq 0)
    {
        Remove-Item -LiteralPath $dataPath -Force
    }

    if (Test-Path -LiteralPath $resolvedTestRoot)
    {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
