#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TRACE_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEST_ROOT"' EXIT

GAME_DIR="$TEST_ROOT/game"
CAPTURE_DIR="$GAME_DIR/scripts/NFSMWAITrace"
STATUS="$CAPTURE_DIR/status.log"
OLD_CAPTURE="capture-20260912-090000-10.bin"
NEW_CAPTURE="capture-20260912-090001-10.bin"
REQUEST="$CAPTURE_DIR/$NEW_CAPTURE.stop.request"
mkdir -p "$CAPTURE_DIR"

printf '%s\r\n' \
  "2026-09-12T09:00:00.000Z INFO tracer-starting $OLD_CAPTURE" \
  "2026-09-12T09:00:01.000Z INFO tracer-starting $NEW_CAPTURE" \
  "2026-09-12T09:00:01.010Z INFO hooks-active $OLD_CAPTURE" \
  >"$STATUS"

(
  for ((attempt = 0; attempt < 100; ++attempt)); do
    [[ -f "$REQUEST" ]] && break
    sleep 0.02
  done
  [[ -f "$REQUEST" ]]
  printf '%s\r\n' \
    "2026-09-12T09:00:01.100Z INFO hooks-active $NEW_CAPTURE" \
    "2026-09-12T09:00:01.110Z INFO stop-request-accepted $NEW_CAPTURE" \
    "2026-09-12T09:00:01.120Z INFO capture-closed $NEW_CAPTURE" \
    >>"$STATUS"
) &
fixture_pid=$!

NFSMW_GAME_DIR="$GAME_DIR" NFSMW_TRACE_STOP_TIMEOUT_SECONDS=5 \
  bash "$TRACE_DIR/stop_capture.sh" >"$TEST_ROOT/output.txt"
wait "$fixture_pid"

[[ -f "$REQUEST" ]]
[[ ! -e "$CAPTURE_DIR/$OLD_CAPTURE.stop.request" ]]
rg -q "capture-closed $NEW_CAPTURE$" "$TEST_ROOT/output.txt"
printf 'stop-capture-contract: PASS\n'
