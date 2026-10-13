#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_DIR="${NFSMW_GAME_DIR:-/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted}"
TARGET="$GAME_DIR/speed.exe"
LOADER="$GAME_DIR/dinput8.dll"
SCRIPTS="$GAME_DIR/scripts"
REPLACE_CONFIG="${NFSMW_TRACE_REPLACE_CONFIG:-0}"

if [[ "$REPLACE_CONFIG" != 0 && "$REPLACE_CONFIG" != 1 ]]; then
  printf 'NFSMW_TRACE_REPLACE_CONFIG must be 0 or 1, got: %s\n' "$REPLACE_CONFIG" >&2
  exit 2
fi

"$SCRIPT_DIR/build.sh"

python3 "$SCRIPT_DIR/validate_target.py" \
  --target "$TARGET" \
  --asi "$SCRIPT_DIR/dist/NFSMWAITrace.asi" \
  --loader "$LOADER" \
  --loader-config-root "$GAME_DIR"

mkdir -p "$SCRIPTS"

install_one() {
  local source="$1"
  local destination="$2"
  if [[ -f "$destination" ]] && cmp -s "$source" "$destination"; then
    printf 'already current: %s\n' "$destination"
    return
  fi
  if [[ -e "$destination" ]]; then
    local stamp
    local backup
    local suffix=0
    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    backup="$destination.backup-$stamp"
    while [[ -e "$backup" ]]; do
      suffix=$((suffix + 1))
      backup="$destination.backup-$stamp-$suffix"
    done
    cp -p "$destination" "$backup"
    printf 'backup: %s\n' "$backup"
  fi
  cp -p "$source" "$destination"
  printf 'installed: %s\n' "$destination"
}

config_is_schema4() {
  awk '
    function trim(value) {
      sub(/^[[:space:]]+/, "", value)
      sub(/[[:space:]]+$/, "", value)
      return value
    }
    {
      line = $0
      sub(/[;#].*$/, "", line)
      if (line ~ /^[[:space:]]*\[/) {
        section = line
        gsub(/[[:space:]]/, "", section)
        in_trace = (tolower(section) == "[trace]")
        next
      }
      if (!in_trace) next
      equals = index(line, "=")
      if (!equals) next
      key = tolower(trim(substr(line, 1, equals - 1)))
      value = trim(substr(line, equals + 1))
      if (key == "schema") {
        schema_count += 1
        schema4 = (value == "4")
      }
    }
    END { exit !(schema_count == 1 && schema4) }
  ' "$1"
}

install_one "$SCRIPT_DIR/dist/NFSMWAITrace.asi" "$SCRIPTS/NFSMWAITrace.asi"

CONFIG_DESTINATION="$SCRIPTS/NFSMWAITrace.ini"
if [[ ! -e "$CONFIG_DESTINATION" ]]; then
  install_one "$SCRIPT_DIR/dist/NFSMWAITrace.ini" "$CONFIG_DESTINATION"
elif cmp -s "$SCRIPT_DIR/dist/NFSMWAITrace.ini" "$CONFIG_DESTINATION"; then
  printf 'already current: %s\n' "$CONFIG_DESTINATION"
elif [[ "$REPLACE_CONFIG" == 0 ]] && config_is_schema4 "$CONFIG_DESTINATION"; then
  printf 'preserved existing tracer config: %s\n' "$CONFIG_DESTINATION"
else
  install_one "$SCRIPT_DIR/dist/NFSMWAITrace.ini" "$CONFIG_DESTINATION"
fi
