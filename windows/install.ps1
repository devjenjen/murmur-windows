# Murmur Windows Per-User Installer
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$publishPath = Join-Path $scriptDir 'artifacts\publish\win-x64'
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\Murmur'
$expectedInstall = Join-Path $env:LOCALAPPDATA 'Programs\Murmur'

if ($installPath -ne $expectedInstall) {
    throw "Target install path mismatch: '$installPath' does not match expected '$expectedInstall'."
}

Write-Host "Publishing Murmur.App (Release win-x64 self-contained)..."
$projectPath = Join-Path $scriptDir 'src\Murmur.App\Murmur.App.csproj'
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Checking for running Murmur instances..."
Get-Process -Name 'Murmur.App' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.MainModule.FileName.StartsWith($installPath, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Stopping running process $($_.Id)..."
            Stop-Process -Id $_.Id -Force
            Start-Sleep -Milliseconds 500
        }
    } catch {
        # Best effort process termination
    }
}

$programsDir = Join-Path $env:LOCALAPPDATA 'Programs'
if (-not (Test-Path $programsDir)) {
    New-Item -ItemType Directory -Path $programsDir -Force | Out-Null
}
if (-not (Test-Path $installPath)) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
}

Write-Host "Installing to $installPath..."
Copy-Item -Path "$publishPath\*" -Destination $installPath -Recurse -Force

$installedExe = Join-Path $installPath 'Murmur.App.exe'
if (-not (Test-Path $installedExe)) {
    throw "Installed executable not found at $installedExe."
}

$publishedExe = Join-Path $publishPath 'Murmur.App.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published executable not found at $publishedExe."
}
$publishedHash = (Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
if ($publishedHash -ne $installedHash) {
    throw "Installed executable hash does not match the published executable."
}

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKeyPath -Force | Out-Null
Set-ItemProperty -Path $runKeyPath -Name 'Murmur' -Value ('"{0}"' -f $installedExe)

Write-Host "Starting installed application..."
Start-Process -FilePath $installedExe

Write-Host "Murmur successfully installed and started at: $installedExe"
