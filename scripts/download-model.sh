#!/bin/bash
# Downloads GigaAM v3 e2e CTC ONNX model for Voicer
# This model produces punctuated, capitalized text directly.
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
MODELS_DIR="$ROOT_DIR/models"
HF_BASE="https://huggingface.co/istupakov/gigaam-v3-onnx/resolve/main"

mkdir -p "$MODELS_DIR"

download_file() {
    local url="$1"
    local outfile="$2"
    if [ -f "$outfile" ]; then
        echo "  Already exists: $outfile"
        return
    fi
    echo "  Downloading: $url"
    echo "  -> $outfile"
    curl -L -o "$outfile" "$url"
    echo "  Done."
}

echo "=== Downloading GigaAM v3 e2e CTC model (int8, ~225 MB) ==="
download_file "$HF_BASE/v3_e2e_ctc.int8.onnx" "$MODELS_DIR/v3_e2e_ctc.int8.onnx"

echo ""
echo "=== Downloading vocabulary ==="
download_file "$HF_BASE/v3_e2e_ctc_vocab.txt" "$MODELS_DIR/v3_e2e_ctc_vocab.txt"

echo ""
echo "=== Patching model metadata for sherpa-onnx compatibility ==="
PATCH_SCRIPT="$SCRIPT_DIR/patch-model-metadata.py"
if [ -f "$PATCH_SCRIPT" ]; then
    if command -v python3 &>/dev/null; then
        python3 "$PATCH_SCRIPT" || {
            echo "WARNING: Model metadata patching failed."
            echo "  You may need to run manually: python3 $PATCH_SCRIPT"
            echo "  Requires: pip install onnx"
        }
    elif command -v python &>/dev/null; then
        python "$PATCH_SCRIPT" || {
            echo "WARNING: Model metadata patching failed."
            echo "  You may need to run manually: python $PATCH_SCRIPT"
            echo "  Requires: pip install onnx"
        }
    else
        echo "WARNING: Python not found. Cannot patch model metadata."
        echo "  Run manually: python3 $PATCH_SCRIPT"
        echo "  Requires: pip install onnx"
    fi
else
    echo "WARNING: patch-model-metadata.py not found at: $PATCH_SCRIPT"
fi

echo ""
echo "All models downloaded to: $MODELS_DIR"
echo "You can now run Voicer."
