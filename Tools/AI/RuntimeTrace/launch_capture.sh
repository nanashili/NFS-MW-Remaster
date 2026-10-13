#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOTTLE="${NFSMW_CX_BOTTLE:-NFS MW}"
CX_ROOT="${CROSSOVER_ROOT:-/Users/tihan-nico/Applications/CrossOver.app/Contents/SharedSupport/CrossOver}"
CX_WINE="${CROSSOVER_WINE:-$CX_ROOT/bin/wine}"
GAME_DIR="${NFSMW_GAME_DIR:-/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted}"
CONFIG="$GAME_DIR/scripts/NFSMWAITrace.ini"

"$SCRIPT_DIR/install.sh"

if ! rg -q '^[[:space:]]*schema[[:space:]]*=[[:space:]]*4[[:space:]]*([;#].*)?$' "$CONFIG"; then
  printf 'Capture config is not schema 4: %s\n' "$CONFIG" >&2
  exit 2
fi
if ! rg -q '^[[:space:]]*enabled[[:space:]]*=[[:space:]]*1[[:space:]]*([;#].*)?$' "$CONFIG"; then
  printf 'Capture is disabled. Set enabled=1 in %s, then run this command again.\n' "$CONFIG" >&2
  exit 2
fi

printf 'Capture status: %s\n' "$GAME_DIR/scripts/NFSMWAITrace/status.log"
printf 'Capture files:  %s\n' "$GAME_DIR/scripts/NFSMWAITrace/capture-*.bin"
printf 'After the scenario, run: %s/stop_capture.sh\n' "$SCRIPT_DIR"
"$CX_WINE" \
  --bottle "$BOTTLE" \
  --check \
  --wait-children \
  --workdir 'C:\Program Files (x86)\NFS Most Wanted' \
  'C:\Program Files (x86)\NFS Most Wanted\speed.exe'
