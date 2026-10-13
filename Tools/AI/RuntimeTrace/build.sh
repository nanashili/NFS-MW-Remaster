#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$SCRIPT_DIR/dist"

python3 "$SCRIPT_DIR/generate_catalogue.py" --check
bash "$SCRIPT_DIR/tests/test_native_contract.sh"
bash "$SCRIPT_DIR/tests/test_stop_capture.sh"

zig cc \
  -target x86-windows-gnu \
  -std=c11 \
  -O2 \
  -mno-sse \
  -mno-sse2 \
  -mno-mmx \
  -mstackrealign \
  -fno-strict-aliasing \
  -Wall \
  -Wextra \
  -Werror \
  -shared \
  "$SCRIPT_DIR/tracer.c" \
  -Wl,--subsystem,windows \
  -o "$SCRIPT_DIR/dist/NFSMWAITrace.asi"

cp "$SCRIPT_DIR/NFSMWAITrace.ini" "$SCRIPT_DIR/dist/NFSMWAITrace.ini"
python3 "$SCRIPT_DIR/validate_target.py" --asi "$SCRIPT_DIR/dist/NFSMWAITrace.asi"
