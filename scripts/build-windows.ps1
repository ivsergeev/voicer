## Voicer Windows Installer Build Script
## Prerequisites: .NET 9 SDK, Inno Setup 6
## Usage: ./scripts/build-windows.ps1 [Configuration] [Runtime]
## Example: ./scripts/build-windows.ps1 Release win-x64

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$root = Split-Path $scriptDir

Write-Host "=== Voicer Windows Installer Build ===" -ForegroundColor Cyan

# 1. Check prerequisites
$iscc = $null
$searchPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "D:\Programs\Inno Setup 6\ISCC.exe"
)
foreach ($p in $searchPaths) {
    if (Test-Path $p) { $iscc = $p; break }
}
if (-not $iscc) {
    Write-Host "ERROR: Inno Setup 6 not found." -ForegroundColor Red
    Write-Host "Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}
Write-Host "Inno Setup: $iscc" -ForegroundColor Green

# 2. Check model files
$modelPath = Join-Path $root "models\v3_e2e_ctc.int8.onnx"
$tokensPath = Join-Path $root "models\v3_e2e_ctc_vocab.txt"
if (-not (Test-Path $modelPath) -or -not (Test-Path $tokensPath)) {
    Write-Host "Model files not found. Downloading..." -ForegroundColor Yellow
    & "$scriptDir\download-model.ps1"
    if (-not (Test-Path $modelPath)) {
        Write-Host "ERROR: Failed to download model files." -ForegroundColor Red
        exit 1
    }
}
Write-Host "Model files: OK" -ForegroundColor Green

# 3. Publish application
Write-Host ""
Write-Host "Publishing application ($Configuration, $Runtime)..." -ForegroundColor Cyan
$publishDir = Join-Path $root "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish "$root\src\Voicer.Desktop\Voicer.Desktop.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed." -ForegroundColor Red
    exit 1
}

$publishSize = (Get-ChildItem $publishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
$exeVersion = (Get-Item "$publishDir\Voicer.exe").VersionInfo.FileVersion
Write-Host "Published: $([math]::Round($publishSize, 1)) MB, version $exeVersion" -ForegroundColor Green

# 4. Build installer
Write-Host ""
Write-Host "Building installer..." -ForegroundColor Cyan
$outputDir = Join-Path $root "output"
if (-not (Test-Path $outputDir)) { New-Item $outputDir -ItemType Directory | Out-Null }

& $iscc "$root\installer\windows\voicer.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed." -ForegroundColor Red
    exit 1
}

$setupFile = Join-Path $outputDir "VoicerSetup.exe"
if (Test-Path $setupFile) {
    $setupSize = (Get-Item $setupFile).Length / 1MB
    Write-Host ""
    Write-Host "=== Build complete ===" -ForegroundColor Green
    Write-Host "Installer: $setupFile" -ForegroundColor Green
    Write-Host "Size: $([math]::Round($setupSize, 1)) MB" -ForegroundColor Green
} else {
    Write-Host "ERROR: Installer file not found." -ForegroundColor Red
    exit 1
}
