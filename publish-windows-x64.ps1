#Requires -Version 5.1
<#
.SYNOPSIS
  Publish Subtitle Edit Release win-x64 and build the Inno Setup installer.

.DESCRIPTION
  1. Syncs installer version from Se.cs
  2. Publishes framework-dependent win-x64 (DLLs beside exe) for local testing
  3. Publishes single-file win-x64 into the Inno Setup bindir
  4. Copies libmpv-2.dll when present
  5. Builds SubtitleEdit-*-Setup.exe with Inno Setup 6

.EXAMPLE
  .\publish-windows-x64.ps1

.EXAMPLE
  .\publish-windows-x64.ps1 -SkipInstaller
#>
[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$KeepRunning,
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Get-Location
}
Set-Location $RepoRoot

$WinX64Out = Join-Path $RepoRoot "src\ui\bin\$Configuration\net10.0\win-x64"
$PublishOut = Join-Path $RepoRoot "src\ui\bin\$Configuration\net10.0\publish"
$UiProject = Join-Path $RepoRoot "src\ui\UI.csproj"
$UpdateVersion = Join-Path $RepoRoot "installer\WindowsInno\update-version.ps1"
$IssPath = Join-Path $RepoRoot "installer\WindowsInno\Subtitle_Edit_Installer.iss"
$Iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$LibMpv = Join-Path $RepoRoot "libmpv-temp\libmpv-2.dll"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Get-AppVersionsFromSe {
    $seCs = Join-Path $RepoRoot "src\ui\Logic\Config\Se.cs"
    $content = Get-Content $seCs -Raw
    $match = [regex]::Match($content, 'public\s+static\s+string\s+Version\s*\{[^}]+\}\s*=\s*"v([^"]+)"')
    if (-not $match.Success) {
        throw "Could not parse Version from Se.cs"
    }

    $versionString = $match.Groups[1].Value  # e.g. 5.1.0-rc17
    $parts = $versionString -split '-', 2
    $numericPart = $parts[0]
    $suffix = if ($parts.Length -gt 1) { $parts[1] } else { "" }
    $fields = $numericPart.Split('.')
    $major = [int]$fields[0]
    $minor = if ($fields.Length -gt 1) { [int]$fields[1] } else { 0 }
    $build = if ($fields.Length -gt 2) { [int]$fields[2] } else { 0 }
    $revMatch = [regex]::Match($suffix, '\d+$')
    $revision = if ($revMatch.Success) { [int]$revMatch.Value } else { 0 }

    [pscustomobject]@{
        Display = $versionString
        Full    = "$major.$minor.$build.$revision"
    }
}

if (-not (Test-Path $UiProject)) {
    throw "UI project not found: $UiProject"
}

if (-not $KeepRunning) {
    Get-Process -Name "SubtitleEdit" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Stopping SubtitleEdit PID $($_.Id) (file lock)..." -ForegroundColor Yellow
        Stop-Process -Id $_.Id -Force
    }
    Start-Sleep -Seconds 1
}

Write-Step "Update installer version from Se.cs"
& $UpdateVersion
$ver = Get-AppVersionsFromSe
Write-Host "Version display=$($ver.Display)  full=$($ver.Full)"

$commonPublishArgs = @(
    $UiProject,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "false",
    "-p:DebugSymbols=false",
    "-p:DebugType=none",
    "-p:Version=$($ver.Full)",
    "-p:FileVersion=$($ver.Full)",
    "-p:InformationalVersion=$($ver.Display)"
)

Write-Step "Publish win-x64 test build (DLLs beside exe)"
if (Test-Path $WinX64Out) {
    Remove-Item -Recurse -Force $WinX64Out
}
dotnet publish @commonPublishArgs -p:PublishSingleFile=false -o $WinX64Out
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (win-x64 test) failed with exit code $LASTEXITCODE"
}
if (Test-Path $LibMpv) {
    Copy-Item $LibMpv (Join-Path $WinX64Out "libmpv-2.dll") -Force
    Write-Host "Copied libmpv-2.dll -> win-x64"
}

Write-Step "Publish win-x64 installer package (single-file)"
if (Test-Path $PublishOut) {
    Remove-Item -Recurse -Force $PublishOut
}
dotnet publish @commonPublishArgs -p:PublishSingleFile=true -o $PublishOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (installer package) failed with exit code $LASTEXITCODE"
}
if (Test-Path $LibMpv) {
    Copy-Item $LibMpv (Join-Path $PublishOut "libmpv-2.dll") -Force
    Write-Host "Copied libmpv-2.dll -> publish"
}

# Strip ONNX natives if any leaked into publish (downloaded on first AttaCut use instead)
Get-ChildItem $PublishOut -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(onnxruntime|DirectML)' } |
    ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "Removed $($_.Name) from publish (on-demand download)"
    }

$exe = Get-Item (Join-Path $WinX64Out "SubtitleEdit.exe")
$bytes = [IO.File]::ReadAllBytes($exe.FullName)
$peOff = [BitConverter]::ToInt32($bytes, 0x3C)
$machine = [BitConverter]::ToUInt16($bytes, $peOff + 4)
if ($machine -ne 0x8664) {
    throw ("Published EXE is not x64 (PE machine=0x{0:X4})" -f $machine)
}
Write-Host ("Test EXE : {0}" -f $exe.FullName)
Write-Host ("  Updated: {0}" -f $exe.LastWriteTime)
Write-Host ("  PE     : x64 (0x8664)")

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Skipped installer (-SkipInstaller)." -ForegroundColor Yellow
    exit 0
}

Write-Step "Build Inno Setup installer"
if (-not (Test-Path $Iscc)) {
    throw "Inno Setup 6 not found at: $Iscc (install 6.7.3+)"
}
if (-not (Test-Path (Join-Path $PublishOut "SubtitleEdit.exe"))) {
    throw "Installer bindir missing SubtitleEdit.exe: $PublishOut"
}

& $Iscc $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setup = Get-ChildItem (Join-Path $RepoRoot "installer\WindowsInno\SubtitleEdit-*-Setup.exe") |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $setup) {
    throw "Setup exe not found under installer\WindowsInno\"
}

$rootCopy = Join-Path $RepoRoot "SubtitleEdit-Windows-x64-Setup.exe"
Copy-Item $setup.FullName $rootCopy -Force

Write-Host ""
Write-Host "DONE" -ForegroundColor Green
Write-Host "  Test build : $WinX64Out\SubtitleEdit.exe"
Write-Host "  Installer  : $($setup.FullName)"
Write-Host "  Also copied: $rootCopy"
