#!/usr/bin/env pwsh
# Downloads GigaAM v3 e2e CTC ONNX model for Voicer
# This model produces punctuated, capitalized text directly.

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$rootDir = Split-Path $scriptDir
$modelsDir = Join-Path $rootDir "models"
$modelFile = Join-Path $modelsDir "v3_e2e_ctc.int8.onnx"
$tokensFile = Join-Path $modelsDir "v3_e2e_ctc_vocab.txt"
$hfBase = "https://huggingface.co/istupakov/gigaam-v3-onnx/resolve/main"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir | Out-Null
}

function Download-File {
    param([string]$Url, [string]$OutFile)
    if (Test-Path $OutFile) {
        Write-Host "  Already exists: $OutFile"
        return
    }
    Write-Host "  Downloading: $Url"
    Write-Host "  -> $OutFile"
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    Write-Host "  Done."
}

Write-Host "=== Downloading GigaAM v3 e2e CTC model (int8, ~225 MB) ==="
Download-File -Url "$hfBase/v3_e2e_ctc.int8.onnx" -OutFile $modelFile

Write-Host ""
Write-Host "=== Downloading vocabulary ==="
Download-File -Url "$hfBase/v3_e2e_ctc_vocab.txt" -OutFile $tokensFile

Write-Host ""
Write-Host "=== Patching model metadata for sherpa-onnx compatibility ==="
$patchScript = Join-Path $scriptDir "patch-model-metadata.py"
if (Test-Path $patchScript) {
    try {
        python $patchScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: Model metadata patching failed (exit code $LASTEXITCODE)."
            Write-Host "  You may need to run manually: python $patchScript"
            Write-Host "  Requires: pip install onnx"
        }
    } catch {
        Write-Host "WARNING: Could not run patch script. Python may not be installed."
        Write-Host "  Run manually: python $patchScript"
        Write-Host "  Requires: pip install onnx"
    }
} else {
    Write-Host "WARNING: patch-model-metadata.py not found at: $patchScript"
}

Write-Host ""
Write-Host "All models downloaded to: $modelsDir"
Write-Host "You can now run Voicer."
