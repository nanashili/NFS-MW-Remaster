#!/usr/bin/env bash
set -euo pipefail

GAME_DIR="${NFSMW_GAME_DIR:-/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted}"
CAPTURE_DIR="$GAME_DIR/scripts/NFSMWAITrace"
STATUS="$CAPTURE_DIR/status.log"
TIMEOUT_SECONDS="${NFSMW_TRACE_STOP_TIMEOUT_SECONDS:-30}"

if [[ ! "$TIMEOUT_SECONDS" =~ ^[0-9]+$ ]] || ((TIMEOUT_SECONDS < 1 || TIMEOUT_SECONDS > 120)); then
  printf 'NFSMW_TRACE_STOP_TIMEOUT_SECONDS must be an integer from 1 to 120.\n' >&2
  exit 2
fi
if [[ ! -f "$STATUS" ]]; then
  printf 'No tracer status log exists: %s\n' "$STATUS" >&2
  exit 1
fi

latest_open_capture() {
  awk '
    function is_open(value) {
      return value == "starting" || value == "active" || value == "stopping"
    }
    { sub(/\r$/, "", $NF) }
    $3 == "tracer-starting" && $4 ~ /^capture-[0-9]+-[0-9]+-[0-9]+[.]bin$/ {
      capture = $4
      if (!(capture in started)) started[capture] = ++start_order
      state[capture] = "starting"
      next
    }
    $4 in started {
      capture = $4
      if ($3 == "hooks-active") state[capture] = "active"
      else if ($3 == "stop-request-accepted") state[capture] = "stopping"
      else if ($3 == "tracer-disabled" || $3 == "tracer-startup-failed" ||
               $3 == "capture-closed" || $3 == "capture-finalization-failed" ||
               $3 == "capture-write-failed-incomplete-prefix") state[capture] = "terminal"
    }
    END {
      newest = 0
      selected = ""
      for (capture in started) {
        if (is_open(state[capture]) && started[capture] > newest) {
          newest = started[capture]
          selected = capture
        }
      }
      if (selected != "") print selected "\t" state[selected]
    }
  ' "$STATUS"
}

capture_state() {
  awk -v selected="$1" '
    { sub(/\r$/, "", $NF) }
    $4 != selected { next }
    $3 == "tracer-starting" { state = "starting"; next }
    $3 == "hooks-active" { state = "active"; next }
    $3 == "stop-request-accepted" { state = "stopping"; next }
    $3 == "capture-closed" { state = "closed"; next }
    $3 == "tracer-disabled" { state = "disabled"; next }
    $3 == "tracer-startup-failed" { state = "startup-failed"; next }
    $3 == "capture-finalization-failed" { state = "finalization-failed"; next }
    $3 == "capture-write-failed-incomplete-prefix" { state = "write-failed"; next }
    END { if (state != "") print state }
  ' "$STATUS"
}

selection="$(latest_open_capture)"
if [[ -z "$selection" ]]; then
  printf 'No active or starting capture is visible in %s\n' "$STATUS" >&2
  exit 1
fi
IFS=$'\t' read -r capture_name initial_state <<<"$selection"
if [[ ! "$capture_name" =~ ^capture-[0-9]{8}-[0-9]{6}-[0-9]+\.bin$ ]]; then
  printf 'Refusing malformed capture identity from status log: %s\n' "$capture_name" >&2
  exit 1
fi

stop_request="$CAPTURE_DIR/$capture_name.stop.request"
if [[ "$initial_state" == "stopping" ]]; then
  printf 'Stop was already accepted for %s; waiting for closure.\n' "$capture_name"
else
  touch "$stop_request"
  printf 'Stop requested for %s: %s\n' "$capture_name" "$stop_request"
fi

attempts=$((TIMEOUT_SECONDS * 10))
for ((attempt = 0; attempt < attempts; ++attempt)); do
  state="$(capture_state "$capture_name")"
  case "$state" in
    closed)
      awk -v selected="$capture_name" '
        { sub(/\r$/, "", $NF) }
        $3 == "capture-closed" && $4 == selected { sub(/\r$/, ""); line = $0 }
        END { if (line != "") print line }
      ' "$STATUS"
      exit 0
      ;;
    disabled|startup-failed|finalization-failed|write-failed)
      awk -v selected="$capture_name" '
        { sub(/\r$/, "", $NF) }
        $4 == selected && ($3 == "tracer-disabled" || $3 == "tracer-startup-failed" ||
                           $3 == "capture-finalization-failed" ||
                           $3 == "capture-write-failed-incomplete-prefix") {
          sub(/\r$/, "")
          line = $0
        }
        END { if (line != "") print line }
      ' "$STATUS" >&2
      exit 1
      ;;
  esac
  sleep 0.1
done

printf 'Timed out before %s closed; keep the game running and inspect %s\n' "$capture_name" "$STATUS" >&2
exit 1
