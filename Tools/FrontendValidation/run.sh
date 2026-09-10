#!/usr/bin/env bash
# Run native Unity tests only in a task-owned copy, never in the user's open project.
set -euo pipefail
source_project="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
workspace=""
prepare_only=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --workspace) [[ $# -ge 2 ]] || { echo '--workspace requires a path' >&2; exit 2; }; workspace="$2"; shift 2 ;;
    --prepare-only) prepare_only=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$workspace" ]]; then
  workspace="$(mktemp -d "${TMPDIR:-/tmp}/mw-frontend-validation.XXXXXX")"
  printf '%s\n' "$source_project" > "$workspace/.mw-frontend-validation-source"
  for directory in Assets Packages ProjectSettings Library; do
    [[ -d "$source_project/$directory" ]] || continue
    if [[ "$(uname -s)" == Darwin ]]; then
      cp -cR "$source_project/$directory" "$workspace/$directory"
    else
      mkdir -p "$workspace/$directory"
      rsync -a "$source_project/$directory/" "$workspace/$directory/"
    fi
  done
else
  workspace="$(CDPATH= cd -- "$workspace" && pwd)"
  [[ "$workspace" != "$source_project" && -f "$workspace/.mw-frontend-validation-source" ]] || { echo 'Not a frontend-owned test workspace.' >&2; exit 2; }
  [[ "$(< "$workspace/.mw-frontend-validation-source")" == "$source_project" ]] || { echo 'Workspace belongs to another project.' >&2; exit 2; }
  case "$workspace/" in "$source_project/"*) echo 'The test workspace must be outside the live project.' >&2; exit 2 ;; esac
  rsync -a "$source_project/Assets" "$source_project/Packages" "$source_project/ProjectSettings" "$workspace/"
fi
mkdir -p "$source_project/Tools/FrontendValidation/Evidence"
printf '%s\n' "$workspace" > "$source_project/Tools/FrontendValidation/validation-workspace.txt"
printf 'Isolated workspace: %s\n' "$workspace"
[[ "$prepare_only" == false ]] || exit 0

version="$(sed -n 's/^m_EditorVersion: //p' "$source_project/ProjectSettings/ProjectVersion.txt")"
editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
[[ -x "$editor" ]] || { echo 'The configured Unity executable is missing.' >&2; exit 2; }
result="$(mktemp -d "$source_project/Tools/FrontendValidation/Evidence/native-$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")"
printf '%s\n' "$result" > "$source_project/Tools/FrontendValidation/latest-tests.txt"
filter="${MW_FRONTEND_TEST_FILTER:-NfsMwRemaster.Driving.Tests.MostWantedFrontend;NfsMwRemaster.Driving.Tests.GameFlowTests}"
args=(-batchmode -nographics -projectPath "$workspace" -job-worker-count 2
  -runTests -testPlatform EditMode -testFilter "$filter" -testResults "$result/tests.xml" -logFile "$result/unity.log")
[[ -z "${UNITY_LICENSE_CHANNEL:-}" ]] || args+=(-licensing-client-channel "$UNITY_LICENSE_CHANNEL")
set +e
"$editor" "${args[@]}"
status=$?
set -e
printf '%s\n' "$status" > "$result/exit-code.txt"
[[ -s "$result/tests.xml" ]] || { echo "Unity produced no test XML. Inspect $result/unity.log" >&2; exit 1; }
python3 - "$result/tests.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print('Native Unity test result:', root.attrib)
for test in root.iter('test-case'):
    if test.get('result') == 'Failed':
        print('FAILED:', test.get('fullname'), test.findtext('failure/message', '').strip())
if int(root.get('failed', '0')) or int(root.get('passed', '0')) == 0:
    raise SystemExit(1)
PY
[[ "$status" -eq 0 ]] || { echo "Unity exited $status; inspect the log." >&2; exit "$status"; }
printf 'Evidence: %s\n' "$result"
