#!/usr/bin/env bash
set -euo pipefail

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TRACE_DIR="$(cd "$TEST_DIR/.." && pwd)"
TRACE_NATIVE_TMP="$(mktemp -d "${TMPDIR:-/tmp}/nfsmw-trace-native.XXXXXX")"
trap 'rm -rf -- "$TRACE_NATIVE_TMP"' EXIT

zig cc \
  -std=c11 \
  -O2 \
  -Wall \
  -Wextra \
  -Werror \
  -I"$TRACE_DIR" \
  "$TEST_DIR/native_contract.c" \
  -o "$TRACE_NATIVE_TMP/native_contract"

"$TRACE_NATIVE_TMP/native_contract"
