#!/usr/bin/env python3
"""
Add required metadata to GigaAM ONNX model for sherpa-onnx compatibility.
sherpa-onnx expects several metadata fields that GigaAM doesn't include:
  - vocab_size: number of output tokens
  - subsampling_factor: encoder subsampling factor (4 for Conformer)
"""

import os
import sys

try:
    import onnx
except ImportError:
    print("ERROR: onnx package is required. Install with: pip install onnx")
    sys.exit(1)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(SCRIPT_DIR)

model_path = os.path.join(ROOT_DIR, "models", "v3_e2e_ctc.int8.onnx")
vocab_path = os.path.join(ROOT_DIR, "models", "v3_e2e_ctc_vocab.txt")

if not os.path.exists(model_path):
    print(f"ERROR: Model not found: {model_path}")
    sys.exit(1)

if not os.path.exists(vocab_path):
    print(f"ERROR: Vocab not found: {vocab_path}")
    sys.exit(1)

# Count vocab size
with open(vocab_path, "r", encoding="utf-8") as f:
    vocab_size = sum(1 for _ in f)

# Metadata required by sherpa-onnx for NeMo CTC models
required_metadata = {
    "vocab_size": str(vocab_size),
    "subsampling_factor": "4",  # GigaAM Conformer uses 4x subsampling
}

print(f"Vocab size: {vocab_size}")
print(f"Loading model: {model_path}")
print(f"  (this may take a minute for a 214 MB model...)")

model = onnx.load(model_path)

# Show existing metadata
existing = {prop.key: prop.value for prop in model.metadata_props}
print(f"\nExisting metadata ({len(existing)} entries):")
for k, v in existing.items():
    print(f"  {k} = {v[:100]}")

# Add missing metadata
added = []
for key, value in required_metadata.items():
    if key in existing:
        print(f"\n  {key} already exists = {existing[key]}")
    else:
        meta = model.metadata_props.add()
        meta.key = key
        meta.value = value
        added.append(f"{key}={value}")
        print(f"\n  Added: {key} = {value}")

if not added:
    print("\nNo changes needed.")
    sys.exit(0)

print(f"\nSaving model with {len(added)} new metadata entries...")
onnx.save(model, model_path)
print(f"Done! Added: {', '.join(added)}")
